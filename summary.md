# PetShopWeb — .NET 10 Upgrade Summary

**Project:** `PetShopWeb/PetShopWeb.csproj` (ASP.NET Core MVC)
**Target:** `net10.0`
**Outcome:** `dotnet build` succeeds with **0 errors, 0 warnings** (Debug and Release), `dotnet publish -c Release` exits 0.

---

## 1. Scope finding

The task was framed as a .NET Framework 4.5–4.8 → `net10.0` migration, but the source project was **already ASP.NET Core** with an SDK-style project file, `Program.cs` minimal hosting, `wwwroot/`, `appsettings.json` and EF Core + ASP.NET Core Identity. A sweep across every `.cs`, `.cshtml`, `.csproj` and `.config` file confirmed **no** occurrences of:

- `System.Web.*`, `System.Web.Http.*`
- `System.Data.Entity` (EF6)
- `HttpContext.Current`, `Server.MapPath`
- `Global.asax`, `Web.config`, `packages.config`
- `[Index]`, `[AllowHtml]`, `[assembly: PreApplicationStartMethod]`

So KB docs 02 (System.Web), 03/04 (MVC/Web API shims), 10 (Global.asax), 11 (Web.config), 16 (EF6→EF Core), 20 (WCF), 21 (System.Drawing), 22 (crypto), 23 (Swagger), 24 (test stack), 25/26 (Web Forms) and 28 (Remoting) had **nothing to apply**. The applicable work was KB 00/01 (project-file cleanup), 27 (build integrity / vulnerability advisories), 19 (containerization) and 08/13 (Identity nullability surface).

The `TargetFramework` and the `Microsoft.*` package versions (`10.0.13`) were already retargeted by the automated pass; everything below is the remaining work needed to make that retarget build clean, secure and runnable.

---

## 2. Changes

**8 files changed · 52 insertions · 100 deletions.** (`PetShopWeb.sln` was retargeted to the SDK-style project GUID by the automated pass, not by this work.)

### Build integrity and packages — `PetShopWeb/PetShopWeb.csproj` (KB 01, 27)

- Removed **44 dead `<Content Remove>` entries** hard-coded to `C:\Users\PC\.nuget\packages\bootstrap\5.2.2\…`. These were a `NETSDK1022` workaround for a `bootstrap` `PackageReference` that no longer exists (client-side assets now come from `libman.json`), and the absolute Windows paths are meaningless on Linux.
- Removed the `<Folder Include="wwwroot\photos\" />` placeholder — the folder has content, so the marker is redundant.
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** It is a design-time scaffolding tool with no runtime or compile role in this project, and it was the sole source of `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 → cleared **2× `NU1901`**.
- **Pinned `System.Security.Cryptography.Pkcs` to `10.0.13`** (direct reference wins over transitive, per KB 27 Step 2). `Microsoft.Data.SqlClient` 6.1.7 — pulled in by the EF Core SQL Server provider — resolved 9.0.11, which carries `GHSA-2fmv-rv2c-59x7` (**`NU1902`**, moderate) and `GHSA-846f-f764-76mx` (**`NU1903`**, high). The pin matches the target runtime major, as the KB requires.

`dotnet list package --vulnerable --include-transitive` now reports **no vulnerable packages**.

### Correctness — `[Required]` was bound to the wrong attribute

- `ViewModels/CreateRoleViewModel.cs`: `using Microsoft.Build.Framework;` → `using System.ComponentModel.DataAnnotations;`

The import made `[Required]` resolve to **MSBuild's** `RequiredAttribute` (a task-parameter marker), not the data-annotations one — so `CreateRoleViewModel.RoleName` carried **no model validation at all**. It compiled only because the scaffolding package leaked `Microsoft.Build.Framework` onto the compile classpath; removing that package surfaced the defect as `CS0246`. This is a **pre-existing** defect, not an upgrade artifact.

**Behavioural change:** `POST /Account/CreateRole` with an empty role name now returns the view with a validation error instead of attempting to create a nameless role. This matches the evident original intent — the property is annotated and `CreateRole.cshtml` renders validation messages.

### Nullability — 22 warnings resolved at the source

`<Nullable>enable</Nullable>` was already set but the code had never been reconciled with it. ASP.NET Core Identity exposes `IdentityRole.Name` and `IdentityUser.UserName` as `string?`, while `FindByIdAsync` requires a non-nullable id and the view-model ids are `string?`.

- `Attributes/ImageExtensionAttribute.cs` — `string path` → `string? path`, matching the `value as string` assignment below it (`CS8600`). The existing `if (path is not null)` guard already handled the null case.
- `Program.cs` — `Configuration["ConnectionStrings:DefaultConnection"]` → `GetConnectionString("DefaultConnection")` with a **fail-fast `InvalidOperationException`**. Misconfiguration now surfaces as a clear startup error instead of a null reaching `UseSqlServer` (`CS8600`).
- `Program.cs` — admin role name extracted to `const string adminRoleName = "admin"`, replacing a nullable `IdentityRole.Name` round-trip (`CS8604`).
- `Controllers/AccountController.cs` — null-id checks placed **before** `FindByIdAsync` on the `EditUser` and `EditRole` POST paths, so a null id falls into the **existing** `NotFound()` / manager-view branch rather than querying the store with a null.
- `Controllers/AccountController.cs` — `EditUsersInRole` POST had a **latent crash**: `FindByIdAsync` returns `IdentityUser?` and the result was passed straight into `IsInRoleAsync`/`AddToRoleAsync`, which throw `ArgumentNullException` on null. Now an unresolvable user is skipped with `continue`, the same control-flow outcome the method already used for users with nothing to change.
- `Controllers/AccountController.cs` — `AddToRolesAsync` was handed `IEnumerable<string?>` (`CS8620`); now filtered to non-null role names.
- Four sites already guarded by `if (ModelState.IsValid)`, plus `role.Name` reads on roles just loaded from the store, use the null-forgiving operator — consistent with the file's existing `editModel.Users!` style.

No `#pragma warning disable` and no `<NoWarn>` were added; every warning was fixed in the code.

### Modernisation — `ASP0014`

- `Program.cs`: `app.UseEndpoints(e => e.MapControllerRoute(…))` → top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`. Route name and template unchanged.

### Robustness — seeding

- `Program.cs`: the admin role is now assigned **only when `userManager.CreateAsync` succeeded**, which also consumes a previously discarded `result`. Before, `AddToRoleAsync` ran even after a failed user creation, where it could only fail.

### Hosting — `Dockerfile`, `docker-compose.yml` (KB 19)

- **The container pipeline could not have run the upgraded app.** Both stages used `sdk:6.0` / `aspnet:6.0`, which cannot host a `net10.0` application. Moved to **`10.0`**.
- Fixed the restore layer: it copied `PetShopWeb/*.csproj` to the image root and then shadowed it with `COPY . .`, leaving `dotnet publish` with two candidate project files in `/app`. Now `restore` and `publish` name `PetShopWeb/PetShopWeb.csproj` explicitly.
- Replaced `EXPOSE 80` / `EXPOSE 433` (the latter a typo for 443, with no certificate configured) with `ASPNETCORE_URLS=http://+:8080` + `EXPOSE 8080` — the non-root convention for .NET 8+ ASP.NET images.
- `docker-compose.yml`: port mapping realigned to `3000:8080` to match the new container port, the stray `3001:433` mapping dropped, and the obsolete `version:` key removed.

---

## 3. Verification

| Check | Result |
| --- | --- |
| `dotnet build PetShopWeb/PetShopWeb.csproj` (clean `obj`/`bin`) | 0 errors · 0 warnings |
| `dotnet build PetShopWeb/PetShopWeb.csproj -c Release` | 0 errors · 0 warnings |
| `dotnet build PetShopWeb.sln` | 0 errors · 0 warnings |
| `dotnet publish -c Release` | exit code 0 |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |
| Razor view compilation | 23 / 23 views present in `PetShopWeb.dll` |
| Legacy-API sweep (`System.Web`, `System.Data.Entity`, `[Index]`, …) | no occurrences |

One infrastructure obstacle, not a code defect: the first build failed with **`MSB3374`** (`…MvcApplicationPartsAssemblyInfo.cache` … `Access to the path … is denied`). The repository tracks `obj/` containing root-owned stale artifacts whose timestamps the build user cannot rewrite. Clearing `obj/` and `bin/` resolved it.

---

## 4. Next steps

### Validation (required before deployment)

1. **Smoke-test the running application.** Nothing has been executed — confidence currently rests on compile, publish and package-audit evidence only. Exercise sign-in/out, registration, catalogue browsing, animal CRUD, comments and the role/user management screens.
2. **Verify the Identity user/role flows specifically.** `AccountController.cs` received the most changes (`EditUser`, `EditRole`, `EditUsersInRole`, `ManageUserRoles`). Confirm the not-found paths still render correctly and that no user or role is silently skipped.
3. **Confirm the new role-name validation** on `POST /Account/CreateRole` — this is the one deliberate behavioural change in this pass.
4. **Confirm EF Core 10 schema generation** against SQL Server. Lazy-loading proxies and the Identity stores crossed several major versions, which is the highest-risk area for silent behavioural drift.
5. **Build and run the container end-to-end** (`docker compose up --build`) and confirm the app serves on `3000 → 8080`.

### Decisions needed (product/human call — deliberately left unchanged)

6. **`ctx.Database.EnsureDeleted()` runs on every startup, in every environment** (`Program.cs`). It **drops the entire database** on each boot, then recreates and re-seeds it. Clearly a development convenience, but it is not gated on the environment. Gate on `app.Environment.IsDevelopment()`, or replace with `ctx.Database.Migrate()` plus EF Core migrations. This is a data-lifecycle decision, not a compile fix.
7. **The active connection string is Windows-only.** `appsettings.json` points `DefaultConnection` at `Server=(localdb)\AspNetCoreCourse`, which cannot work in a Linux container; the container-appropriate value sits **commented out** immediately above it. Decide where each value belongs — the `localdb` one in `appsettings.Development.json`, the container one supplied as `ConnectionStrings__DefaultConnection` from a secret store rather than committed. Not changed here because it would mean committing a plain-text SQL password that is currently only a comment.
8. **Hard-coded seeded admin credentials** (`admin@admin.com` / `Admin123`, with `RequireNonAlphanumeric = false`). Move to configuration/user-secrets and force a password change on first sign-in.
9. **`docker-compose.yml` still references pre-migration image tags** (`yarins2000/petshopapp:1.0`, `yarins2000/petshopdb:1.0`). These need rebuilding and republishing from the updated `Dockerfile`; the `db` image was not inspected in this pass. Whether the service should switch to `build: .` instead of a registry image is a deployment-workflow decision.

### Recommended improvements

10. **Add a test project.** The solution has none, so this upgrade is verified by compilation alone. Integration tests over the Identity flows would de-risk future runtime-major jumps.
11. **Untrack `obj/` and `bin/`.** They are committed despite `.gitignore` coverage and caused the `MSB3374` first-build failure.
12. **Consider `<TreatWarningsAsErrors>`** now that the project is at zero warnings, to prevent regression.
13. **Tidy `launchSettings.json`** — the `IIS Express` profile and `iisSettings` block are dead on a Linux/Kestrel deployment.
14. **Review `AccountRepository`'s `GetUsers`/`GetRoles`** and the `EditUsersInRole` POST loop: the loop's `result.Succeeded` handling returns on the last index regardless of outcome, so individual failures are not surfaced to the user. Pre-existing and out of scope for a build-correctness pass.
