# PetShopWeb — .NET 10 (net10.0) Migration Summary

**Target:** `./PetShopWeb/PetShopWeb.csproj` → `net10.0`
**Final state:** `dotnet build` exits **0** with **0 errors and 0 warnings** (verified clean, from-scratch, in both `Debug` and `Release`, and at the solution level via `PetShopWeb.sln`).

---

## Starting point

CodeKB (index of the pre-upgrade source) and the working tree agreed that this application was **already an ASP.NET Core MVC app** — `Program.cs` minimal hosting, `wwwroot`, `appsettings.json`, EF Core + ASP.NET Core Identity, constructor-injected repositories. A prior automated pass had already moved the project to SDK-style `net10.0` and swapped the solution project-type GUID to the SDK C# GUID.

Consequently there was **no System.Web / Global.asax / Web.config / EF6 / WebForms / WCF / ASMX surface to migrate**. This was verified rather than assumed — a repo-wide scan for `System.Web*`, `System.Data.Entity`, `System.Web.Http*`, `HtmlTextWriter`, `CacheDependency`, `IRegisteredObject`, `[AllowHtml]`, `[Index]`, `[assembly: PreApplicationStartMethod]` and for `*.config` / `Global.asax*` / `packages.config` / `*.aspx` / `*.asmx` files returns **nothing**. KB docs 02, 03, 10, 11, 16, 25, 26, 20, 33 therefore do not apply to this codebase.

The work in this cycle was accordingly: unblock the build, then eliminate every warning.

---

## Changes made

### 1. Build blocker — `MSB3374` (stale, root-owned intermediate output)

The only hard error was not a code error:

```
error MSB3374: The last access/last write time on file
"obj/Debug/net10.0/PetShopWeb.MvcApplicationPartsAssemblyInfo.cache" cannot be set.
Access to the path '...' is denied.
```

`PetShopWeb/obj` and `PetShopWeb/bin` were committed/left over from a previous run and owned by `root`, so the Razor `MvcApplicationPartsDiscovery` target could not stamp its cache file. Removed both directories; the build then restored and compiled cleanly. No project or code change was needed for this.

### 2. `PetShopWeb.csproj` — project integrity (KB 01, 06, 27)

- **Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items.** These were absolute Windows paths into a developer's local NuGet cache, left behind after `bootstrap` stopped being a `PackageReference` (client-side assets now come from `libman.json` / `package.json` into `wwwroot/lib`). They matched nothing and are meaningless on Linux.
- **Removed `<Folder Include="wwwroot\photos\" />`** — a Windows-separator placeholder item for a folder that now exists with content.
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** This is a *design-time scaffolding* package (`dotnet aspnet-codegenerator`) that is not required to build or run the app, and it was the **sole source of 3 of the 4 vulnerability advisories** — it dragged in the whole `NuGet.*` 6.12.1 client stack (`NuGet.Packaging`, `NuGet.Protocol` → `NU1901`) and an old `System.Security.Cryptography.Pkcs`. KB 27 precedence is *remove over silence*, so it was removed rather than pinned. If scaffolding is wanted later, install it as a local tool instead of a package reference so it never reaches the app's dependency graph.
- **Pinned `System.Security.Cryptography.Pkcs` to `10.0.14`** (direct top-level reference, per KB 27 Step 2/3). After the above removal, `dotnet nuget why` showed the remaining `NU1902`/`NU1903` advisories came in transitively: `Microsoft.EntityFrameworkCore.SqlServer 10.0.14` → `Microsoft.Data.SqlClient 6.1.7` → `System.Security.Cryptography.Pkcs 9.0.11` (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7). A direct reference wins over the transitive one; `10.0.14` matches the target runtime major, per the KB 08 runtime-major rule. The reference carries a comment explaining why it exists so it is not "tidied away" later.

### 3. `ViewModels/CreateRoleViewModel.cs` — real validation bug (behavioral fix)

```diff
-using Microsoft.Build.Framework;
+using System.ComponentModel.DataAnnotations;
```

`RoleName` was decorated with `[Required]`, but the only `using` in scope was `Microsoft.Build.Framework` — so it bound to **MSBuild's** `RequiredAttribute` (a task-parameter marker), not the Bean-style validation attribute. MVC never saw a validation rule, so `ModelState.IsValid` in `AccountController.CreateRole` passed for an empty role name. It compiled only because `Microsoft.Build.Framework` leaked in transitively through the scaffolding package removed above.

**This is a behavioral change, and an intended one:** creating a role with a blank name is now correctly rejected and redisplays the form with a validation message, consistent with every other view model in the project.

### 4. Nullable-reference correctness — `CS8600` / `CS8604` / `CS8620` (21 warnings)

The project has `<Nullable>enable</Nullable>`, and these were genuine unchecked-null paths, not noise. Resolved by matching each site to the guarantee that actually holds there, rather than blanket-suppressing:

- **`Program.cs`** — `builder.Configuration["ConnectionStrings:DefaultConnection"]` (returns `string?`) replaced with `GetConnectionString("DefaultConnection")` plus an explicit `?? throw new InvalidOperationException(...)`. Misconfiguration now fails fast at startup with a clear message instead of surfacing later as an opaque `UseSqlServer(null)` failure.
- **`Program.cs` seeding** — replaced `adminRole.Name` (nullable `IdentityRole.Name`) with a `const string adminRoleName = "admin"`, used for both role creation and the role assignment. Also dropped an unused `result` local. Same seeded data as before.
- **`AccountController.cs`**
  - Inside `if (ModelState.IsValid)` the `[Required]` members are guaranteed non-null, so `Login`/`Register` use the null-forgiving operator (`loginmodel.Email!`, `registermodel.Password!`).
  - `IdentityRole.Name` / `IdentityUser.UserName` after a successful `FindBy*` lookup likewise use `!`.
  - Where a nullable id came straight off a posted view model with **no** prior guarantee, a real guard was added instead of `!`: `EditUser(EditUserViewModel)` and `EditRole(EditRoleViewModel)` short-circuit a null `Id` into the existing not-found branch (`NotFound()` / `RolesManager`) rather than letting `FindByIdAsync(null)` throw `ArgumentNullException`. This turns a 500 into the already-intended not-found response.
  - `EditUsersInRole` (POST): a null `model[i].UserId`, or an id that resolves to no user, now `continue`s past that row instead of passing `null` into `IsInRoleAsync`/`AddToRoleAsync`. `result` was also tightened from `IdentityResult?` to `IdentityResult` (both assignments are non-null).
  - `ManageUserRoles` (POST): `AddToRolesAsync` was handed an `IEnumerable<string?>`; now filters `x.RoleName is not null` before projecting, so a malformed post cannot push a null role name into Identity.
- **`Attributes/ImageExtensionAttribute.cs`** — `string path;` → `string? path;`. `path = value as string` can legitimately be null and the very next line already tested `path is not null`; the declaration simply didn't admit it.

### 5. `Program.cs` — `ASP0014` and middleware ordering

Replaced the obsolete `UseEndpoints` wrapper with top-level route registration, and moved `UseAuthentication()` to **after** `UseRouting()`:

```csharp
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}");
```

This is the canonical ASP.NET Core order. `UseAuthorization` was already correctly placed after `UseRouting` (so endpoint metadata such as `[Authorize(Roles = "admin")]` was being honoured); the route table and the auth pipeline behave as before.

`UseStaticFiles()` was deliberately **kept** rather than switched to the newer `MapStaticAssets()`. `MapStaticAssets` serves from a build-time generated manifest, which would not serve the animal photos uploaded into `wwwroot/photos` at runtime. `UseStaticFiles` is the correct choice for this app and is not deprecated.

### 6. Minor cleanup

Removed two dead `using` directives that no longer resolved to anything used: `System.Data` (`AccountController.cs`) and `System.Security` (`EditRoleViewModel.cs`).

---

## Verification performed

| Check | Result |
|---|---|
| `dotnet build PetShopWeb/PetShopWeb.csproj` (Debug) | exit 0 — 0 errors, 0 warnings |
| `dotnet build PetShopWeb/PetShopWeb.csproj -c Release`, after `rm -rf obj bin` | exit 0 — 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln` | exit 0 — 0 errors, 0 warnings |
| Razor view compilation | all 21 `.cshtml` views plus `_ViewImports` / `_ViewStart` compiled into `PetShopWeb.dll` — views are error-free, not merely un-checked |
| Legacy-API scan | no `System.Web*`, `System.Data.Entity`, `System.Web.Http*`, or obsolete-API references; no `*.config` / `Global.asax` / `packages.config` / `*.aspx` / `*.asmx` files |
| Unit tests | **none exist** — the solution contains a single web project and no test project, so there was nothing to run. See Next steps. |

The application was intentionally **not executed**, per the standing instruction to validate via build and tests rather than by running the app.

---

## Next steps (non-blocking — for human review)

1. **`EnsureDeleted()` on every startup is destructive and unguarded.** `Program.cs` calls `ctx.Database.EnsureDeleted()` immediately followed by `EnsureCreated()` *unconditionally*, outside any environment check. In Development this is a convenient reset; **in Staging/Production this drops the entire PetShop database on every process start**, including on an autoscale event or container restart. This is pre-existing behavior and changing it alters runtime semantics well beyond the framework migration, so it was left intact. Strongly recommend gating it on `app.Environment.IsDevelopment()` and moving schema management to EF Core migrations (`dotnet ef migrations add` + `Database.Migrate()`), which is also the only path that preserves data across deployments. Requires a product decision on the dev-reset workflow.
2. **Seeded admin credentials are hard-coded.** `admin@admin.com` / `Admin123` is committed in `Program.cs`, and `options.Password.RequireNonAlphanumeric = false` weakens the password policy globally. Move the seed credentials to user-secrets / environment configuration and restrict seeding to non-production environments.
3. **The seeding guard looks wrong.** The user seed is guarded by `!ctx.Users.Any(u => u.UserName == "Admin")` but the user it creates has `UserName == "admin@admin.com"`, so the two never match. Today this is masked entirely by the `EnsureDeleted()` above (the table is always empty); it would misbehave as a duplicate-insert attempt the moment item 1 is fixed. Fix alongside item 1.
4. **The active connection string targets Windows LocalDB.** `appsettings.json` uses `Server=(localdb)\AspNetCoreCourse;Trusted_Connection=True`, which cannot work on Linux or in the provided `Dockerfile`/`docker-compose.yml`; the container-appropriate `Server=tcp:db,1433;...` string sits commented out directly above it. Decide the per-environment story — the SQL Server connection string belongs in `appsettings.Development.json` / environment variables rather than in the shared `appsettings.json`, and `appsettings.Development.json` currently carries no `ConnectionStrings` section at all. Note that `Program.cs` now throws a clear `InvalidOperationException` at startup if the string is absent, so a missing value will be obvious rather than silent.
5. **No automated tests exist.** There is no test project, so the migration is verified by compilation (including Razor view compilation) only — there is no behavioral regression safety net. Worth adding a test project with coverage of the repositories and the `AccountController` role/user management flows, which hold the bulk of the application's logic and received the most edits in this pass. KB 24 covers the test-stack setup.
6. **Scaffolding tooling, if still wanted.** `Microsoft.VisualStudio.Web.CodeGeneration.Design` was removed as a package reference (see §2). If the team still scaffolds controllers/views, install it as a *local tool* (`dotnet tool install dotnet-aspnet-codegenerator`) so the `NuGet.*` client stack and its advisories stay out of the application's dependency graph.
7. **Revisit the pinned `System.Security.Cryptography.Pkcs` reference.** It exists only to override a vulnerable transitive version from `Microsoft.Data.SqlClient 6.1.7`. Once a `Microsoft.EntityFrameworkCore.SqlServer` / `Microsoft.Data.SqlClient` update ships that resolves a patched `Pkcs` on its own, this explicit reference can be dropped.
8. **Committed build output.** `PetShopWeb/obj` and `PetShopWeb/bin` were present in the working tree and root-owned, which is what caused the only build error in this cycle (§1). Add them to `.gitignore` so this cannot recur.
