# PetShopWeb — .NET 10 migration summary

Target: `./PetShopWeb/PetShopWeb.csproj` → `net10.0` (SDK-style, `Microsoft.NET.Sdk.Web`).

## Build status

| Check | Result |
|---|---|
| `dotnet build PetShopWeb/PetShopWeb.csproj` (Debug) | 0 errors, 0 warnings |
| `dotnet build PetShopWeb/PetShopWeb.csproj -c Release` | 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln` | 0 errors, 0 warnings |
| `dotnet publish -c Release` | succeeds |

## Starting state

The project was already SDK-style on `net10.0` with framework-tied packages at `10.0.12`, and it
compiled. There were no `System.Web.*`, `System.Data.Entity`, `System.Web.Http.*`, EF6, MVC5, OWIN,
Global.asax, Web.config, WCF, System.Drawing, or Web Forms artifacts left to migrate — the app is
ASP.NET Core MVC + EF Core + ASP.NET Core Identity throughout. Work in this cycle was therefore
project-file hygiene, package integrity, and clearing all 23 outstanding warnings.

## Changes

### Project file (`PetShopWeb.csproj`)
- Removed a 44-item `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\..." />` block:
  hard-coded Windows paths for a `bootstrap` NuGet package that is no longer referenced (client
  assets live in `wwwroot/lib`). Dead on any OS, and unusable on Linux.
- Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2. It is a design-time scaffolding
  tool, not a build or runtime dependency, and it was the sole source of both `NU1901` advisories
  (transitive `NuGet.Packaging` / `NuGet.Protocol` 6.12.1). It also pulled Roslyn, MSBuild,
  Newtonsoft.Json and Azure.Identity into the compile closure. Per KB 27 (blocked packages and
  build integrity), removing the offending package is preferred over silencing the advisory.
  Re-add it locally with `dotnet add package` only when running `aspnet-codegenerator`.
- Dropped the now-redundant `<Folder Include="wwwroot\photos\" />` (the folder has content).

### Latent bug fixed by the package removal
`ViewModels/CreateRoleViewModel.cs` imported `[Required]` from **`Microsoft.Build.Framework`** — the
MSBuild task-parameter attribute, not `System.ComponentModel.DataAnnotations`. It compiled only
because the scaffolding package leaked `Microsoft.Build.Framework` into the reference set, and it
meant `RoleName` was **never actually validated**. Switched to
`System.ComponentModel.DataAnnotations`, so `CreateRole` now rejects an empty role name as intended.
This is a deliberate behavioral change (validation now works).

### `Program.cs`
- `builder.Configuration["ConnectionStrings:DefaultConnection"]` → `GetConnectionString("DefaultConnection")`
  with a fail-fast `InvalidOperationException` when absent (clears `CS8600`; a missing connection
  string now produces a clear startup error instead of a later null-argument failure).
- Admin-role seeding uses a `const string adminRoleName = "admin"` instead of the nullable
  `IdentityRole.Name` (clears `CS8604`). Same role name, same seed behavior.
- `app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))` → top-level
  `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")` (clears
  `ASP0014`). Identical route template and matching behavior.

### `Controllers/AccountController.cs` (14 nullable warnings)
- `Login` / `Register`: `Email`/`Password` passed with `!` — inside `if (ModelState.IsValid)`, so
  `[Required]` has already guaranteed them.
- `EditUser` (POST), `EditRole` (POST): nullable view-model ids passed as `id ?? string.Empty`; a
  non-resolving id still falls through to the existing `NotFound()` path.
- `EditRole`, `EditUsersInRole`, `ManageUserRoles`: nullable `IdentityRole.Name` / `IdentityUser.UserName`
  hoisted into locals coalesced to `string.Empty`.
- `EditUsersInRole` (POST): a user id that no longer resolves is now skipped (`if (user is null) continue;`)
  instead of being dereferenced. This previously threw a `NullReferenceException` on a stale POST —
  a deliberate behavioral fix.
- `ManageUserRoles` (POST): null role names are filtered before `AddToRolesAsync` (clears `CS8620`).

### `Attributes/ImageExtensionAttribute.cs`
`string path` → `string? path`, matching the `value as string` assignment and the existing
`if (path is not null)` guard (clears `CS8600`). Validation outcome unchanged.

### `Dockerfile`
Base images `sdk:6.0` / `aspnet:6.0` → `10.0` — a .NET 6 runtime cannot host a `net10.0` app. Also
made restore/publish reference `PetShopWeb/PetShopWeb.csproj` explicitly (the old `COPY PetShopWeb/*.csproj .`
plus `COPY . .` left two candidate projects in the build context) and set `ASPNETCORE_HTTP_PORTS=80`,
because .NET 8+ container images default to port 8080 while `docker-compose.yml` maps `3000:80`.
Not verified by a container build in this environment.

## Next steps (non-blocking)

- **`ctx.Database.EnsureDeleted()` runs on every startup** in `Program.cs`, unconditionally — it drops
  and recreates the database each time the app boots, including in production. Left as-is to preserve
  existing behavior, but it should be gated to Development and replaced by
  `ctx.Database.Migrate()` with EF Core migrations elsewhere. Needs a product decision.
- **Seeded admin credentials are hard-coded** (`admin@admin.com` / `Admin123`) and the SQL Server
  connection string with a `sa` password is committed in `appsettings.json`. Move to user secrets /
  environment variables and force a password change on first login.
- **No test project exists** in the solution, so the behavioral fixes above (role-name validation,
  null-user skip, fail-fast connection string) are covered by compilation only. Adding an xUnit
  project needs NuGet access for `xunit` / `Microsoft.NET.Test.Sdk`, which this offline cycle did
  not have; per KB 24 it should be added when the feed is reachable.
- **`docker-compose.yml` still pins prebuilt images** `yarins2000/petshopapp:1.0` / `petshopdb:1.0`
  (built on .NET 6). They must be rebuilt and re-tagged from the updated Dockerfile, and the stray
  `3001:433` mapping (likely a typo for `443`) reviewed. See KB 19 for Linux containerization.
- **`app.UseStaticFiles()` could become `app.MapStaticAssets()`** on .NET 10 for fingerprinting and
  precompression (KB 09), but that changes asset URLs in the Razor views, so it was left out of a
  warning-cleanup pass.
- **No authentication is required for static assets or the Home/Catalog routes** — unchanged from the
  original app and intended for a public catalog; noted only so it stays a conscious choice.
