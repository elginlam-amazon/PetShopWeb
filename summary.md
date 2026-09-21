# PetShopWeb — .NET → `net10.0` Migration Summary

**Target:** `net10.0`
**Project:** `./PetShopWeb/PetShopWeb.csproj`
**Result:** `dotnet build` succeeds — **0 errors, 0 warnings** (verified for `Debug` and `Release`, and for `PetShopWeb.sln`).

---

## Starting point

`PetShopWeb` was already an SDK-style ASP.NET Core MVC application (`Microsoft.NET.Sdk.Web`, previously
`net6.0`) — **not** a .NET Framework / `System.Web` application. The pre-build cleanup pass
(KB `00-prebuild-cleanup-and-bulk-fixes.md`) was therefore a verified no-op; the repository was confirmed
to contain **no** references to:

- `System.Web.*`, `System.Web.Http.*`, `System.Data.Entity`
- `[Index]`, `[AllowHtml]`, `[assembly: PreApplicationStartMethod]`
- `CacheDependency`, `IRegisteredObject`, `HtmlTextWriter`
- `Web.config` / `Global.asax` (configuration already lives in `appsettings.json` + `Program.cs`)

The migration work was consequently a **framework/TFM upgrade plus package, API and container
modernisation**, and driving the warning count to zero.

---

## Changes applied

### 1. Project file — `PetShopWeb/PetShopWeb.csproj`
(KB `01-project-file-migration.md`, `06-package-migrations.md`, `27-blocked-packages-and-build-integrity.md`)

- `TargetFramework`: `net6.0` → **`net10.0`**.
- Framework-tied packages aligned to the target runtime major (`10.0.x`), per the KB runtime-major rule:
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `6.0.11` → `10.0.12`
  - `Microsoft.EntityFrameworkCore.Proxies` `7.0.0` → `10.0.12`
  - `Microsoft.EntityFrameworkCore.SqlServer` `7.0.0` → `10.0.12`
  - `Microsoft.EntityFrameworkCore.Tools` `6.0.11` → `10.0.12`
  (this also removed the pre-existing split EF version set: Identity/Tools were on 6.0 while the
  provider was on 7.0.)
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design`.** This is a design-time scaffolding-only
  package; it was the transitive source of the four `NU1901` advisories
  (`NuGet.Packaging` / `NuGet.Protocol` 6.12.1, GHSA-g4vj-cjjj-v7hg) and it was shipping the whole
  NuGet-client / Roslyn / MSBuild stack into the web application's output. Per the KB precedence
  ("remove/replace a legacy package over silencing the warning") it was removed rather than pinned.
  *Effect:* build output drops from ~100 to 47 assemblies and the advisories are gone. Nothing in the
  application referenced it after the fix in §3. Scaffolding, if ever needed again, is available via the
  `dotnet-aspnet-codegenerator` / `dotnet scaffold` tool rather than a project reference.
- **Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items.** These
  pointed at a hard-coded Windows developer path for a `bootstrap` NuGet package that is no longer
  referenced (client-side assets come from `libman.json` / `package.json`). They were no-ops and are
  meaningless on Linux.

### 2. `Program.cs` (KB `10-global-asax-migration.md`, `11-configuration-migration.md`)

- Connection string is now read with `Configuration.GetConnectionString("DefaultConnection")` and fails
  fast with a clear `InvalidOperationException` if absent, instead of silently passing `null` into
  `UseSqlServer` (resolved `CS8600`).
- Admin-seed role name hoisted into a `const string adminRoleName = "admin"` and used for both role
  creation and `AddToRoleAsync`, instead of reading back the nullable `IdentityRole.Name`
  (resolved `CS8604`). Identical seeded data.
- `app.UseEndpoints(e => e.MapControllerRoute(...))` → top-level `app.MapControllerRoute(...)`
  (resolved `ASP0014`). Same route template, same middleware order (`UseRouting` is still explicit).

### 3. `ViewModels/CreateRoleViewModel.cs` — **behavioural fix**

`RoleName` was decorated with `[Required]` resolved from **`using Microsoft.Build.Framework;`** —
i.e. `Microsoft.Build.Framework.RequiredAttribute`, an MSBuild *task* attribute that is not a
`ValidationAttribute`. It compiled only because the scaffolding package (§1) happened to drag
`Microsoft.Build.Framework.dll` onto the compile path, and it performed **no validation at runtime**.
Changed to `using System.ComponentModel.DataAnnotations;`, which is plainly what was intended.

> **Behavioural change:** creating a role with an empty `RoleName` is now rejected by model validation
> (`ModelState.IsValid == false`) and the `CreateRole` view is redisplayed, instead of the empty name
> being passed to `RoleManager.CreateAsync` and rejected later by Identity. `Views/Account/CreateRole.cshtml`
> already renders the validation message, so no view change was needed.

### 4. Nullability hardening (`Nullable` is `enable` on this project)

`Controllers/AccountController.cs` — all 17 `CS8604` / `CS8620` warnings resolved:

- `[Required]` view-model members used inside an `if (ModelState.IsValid)` block are passed with the
  null-forgiving operator (`loginmodel.Email!`, `registermodel.Password!`) — no behavioural change.
- `IdentityRole.Name` is now null-checked at the single lookup site (`if (role?.Name is null)`) and
  captured into a local `roleName`, replacing repeated nullable member access. The `null` branch takes
  the **same** path the code already took for a missing role (`NotFound()` / `RolesManager` view).
- Ids arriving from POST bodies (`EditUserViewModel.Id`, `EditRoleViewModel.Id`,
  `UserRoleViewModel.UserId`) are null-checked before `FindByIdAsync`. **Robustness fix:** a crafted or
  malformed POST previously produced an unhandled `ArgumentNullException` (HTTP 500); it now takes the
  existing "not found" / "skip this row" path.
- `EditUsersInRole (POST)` additionally skips rows whose user id does not resolve to a user, instead of
  passing `null` into `UserManager.IsInRoleAsync`.
- `AddToRolesAsync` is given `IEnumerable<string>` by filtering out `null` role names rather than
  passing `IEnumerable<string?>` (resolved `CS8620`).

`Attributes/ImageExtensionAttribute.cs` — `string path;` → `string? path;`, matching the `value as string`
assignment and the existing `if (path is not null)` guard that follows (resolved `CS8600`). No behavioural
change.

### 5. Container — `Dockerfile` (KB `19-linux-containerization.md`)

- Base images `sdk:6.0` / `aspnet:6.0` → **`sdk:10.0` / `aspnet:10.0`** to match the target runtime.
  Without this the container build would fail outright on the `net10.0` project.
- Build stage now restores and publishes an **explicit project path** (`PetShopWeb/PetShopWeb.csproj`)
  from `/src`. Previously the csproj was copied to the same directory that `COPY . .` later filled with
  `PetShopWeb.sln`, leaving `dotnet publish` with both a project *and* a solution file in its working
  directory — an ambiguous, break-prone build.
- Added `ENV ASPNETCORE_HTTP_PORTS=80`. The .NET 8+ runtime images default to listening on **8080**;
  this pins the container back to **80** so the existing `docker-compose.yml` mapping (`3000:80`) and the
  published image's port contract keep working unchanged.

---

## Verification

| Check | Result |
| --- | --- |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj` (Debug) | **succeeded** — 0 errors, 0 warnings |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj -c Release` (clean `bin`/`obj`) | **succeeded** — 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln` | **succeeded** — 0 errors, 0 warnings |
| `NU****` / `NETSDK****` warnings | none remain |
| Razor view compilation (build-time, Web SDK) | all 22 `.cshtml` files compile |

There are no test projects in this repository, so there was no test suite to run. Per instructions, the
compiled application was not executed.

---

## Next steps (non-blocking — for human review)

1. **`Program.cs` calls `ctx.Database.EnsureDeleted()` on every start.** This drops and recreates the
   database (and re-seeds the `admin@admin.com` / `Admin123` account) on each launch. That is
   pre-existing behaviour and was deliberately left untouched, but it is unsafe outside a local dev
   environment. Recommended: guard it with `if (app.Environment.IsDevelopment())`, or replace
   `EnsureDeleted`/`EnsureCreated` with `Database.Migrate()` plus EF Core migrations (KB
   `16-ef6-to-efcore-migration.md`). Needs a product decision about dev/test/prod behaviour.
2. **Seeded admin credentials are hard-coded** in `Program.cs` (`Admin123`), and
   `appsettings.json` contains a committed `Trusted_Connection` LocalDB connection string plus a
   commented-out SQL Server connection string with an inline `sa` password. Move both to user-secrets /
   environment variables (`ConnectionStrings__DefaultConnection`) per KB `11-configuration-migration.md`
   and `19-linux-containerization.md` §4. Not changed here because it alters deployment inputs.
3. **`appsettings.json` uses `//` comments**, which are tolerated by the JSON configuration provider but
   are not valid strict JSON. Harmless today; worth removing if any other tooling reads the file.
4. **`Microsoft.EntityFrameworkCore.Tools` still contributes design-time assemblies to the build output**
   (`Microsoft.EntityFrameworkCore.Design`, `Microsoft.CodeAnalysis.*`, `Microsoft.Build.Framework`), a
   consequence of `runtime` being listed in its `IncludeAssets` — the VS-generated default. This emits no
   warning and was left alone to avoid breaking `dotnet ef migrations`. It could be trimmed by switching
   to a `PrivateAssets="all"` reference to `Microsoft.EntityFrameworkCore.Design` only.
5. **Middleware order:** `app.UseAuthentication()` currently runs *before* `app.UseRouting()`. This works
   and is pre-existing, but the recommended ASP.NET Core order is
   `UseRouting` → `UseAuthentication` → `UseAuthorization`. Left unchanged to avoid altering behaviour of
   a working pipeline.
6. **Static image paths.** Seeded `Animal.ImagePath` values use the Web Forms–style `~/photos/...` prefix
   and are rendered from a model expression, where Razor's `UrlResolutionTagHelper` does not expand `~/`
   (it only rewrites literal attribute values). This is pre-existing and unrelated to the TFM upgrade, but
   worth confirming images render — consider storing paths as `/photos/...`.
7. **`docker-compose.yml`** pins prebuilt images (`yarins2000/petshopapp:1.0`) rather than `build: .`, and
   maps `3001:433` (`433` appears to be a typo for `443`, and no HTTPS endpoint is configured). The port
   contract was deliberately preserved by the `ASPNETCORE_HTTP_PORTS=80` setting above; republishing the
   image under a new tag and correcting the port are deployment decisions.
8. **`Properties/launchSettings.json` retains an `IIS Express` profile.** Harmless (it is dev-only and
   ignored on Linux) but now dead weight; the `PetShopWeb` Kestrel profile is the one that applies.
