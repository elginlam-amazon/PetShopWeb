# PetShopWeb — .NET Framework → net10.0 migration summary

Target project: `./PetShopWeb/PetShopWeb.csproj` → `net10.0`

## Final build status

| Check | Result |
| --- | --- |
| `dotnet build PetShopWeb/PetShopWeb.csproj` (Debug) | **0 errors, 0 warnings** |
| `dotnet build PetShopWeb/PetShopWeb.csproj -c Release` | **0 errors, 0 warnings** |
| `dotnet build PetShopWeb.sln` | **0 errors, 0 warnings** |
| `dotnet publish -c Release` | **exit code 0** |
| `dotnet list package --vulnerable --include-transitive` | **no vulnerable packages** |
| Razor view compilation | **23/23 views compiled** into `PetShopWeb.dll` |

## Starting state

The project file had already been converted to SDK-style `net10.0` with
`Microsoft.AspNetCore.*` / `Microsoft.EntityFrameworkCore.*` 10.0.13 packages, and the
application code was already on ASP.NET Core idioms (`Program.cs` top-level statements,
`IActionResult`, `IWebHostEnvironment`, EF Core `DbContext`, ASP.NET Core Identity,
`wwwroot/` static files). Verified absent across all `.cs`, `.cshtml`, `.csproj` and
`.config` files: `System.Web.*`, `System.Data.Entity`, `System.Web.Http.*`,
`HtmlTextWriter`, `CacheDependency`, `IRegisteredObject`, `PreApplicationStartMethod`,
`[AllowHtml]`, `[Index]`.

The initial build failed on a single infrastructure error (`MSB3374`: the committed
`obj/` directory contained root-owned stale artifacts whose timestamps could not be
rewritten). Removing `obj/` and `bin/` cleared it, revealing **0 compilation errors and
27 warnings**. The remaining work was therefore warning elimination and finishing the
parts of the upgrade the project-file conversion had not reached.

## Changes made

### Package and build integrity (KB 06, 27) — cleared 4 `NU` warnings

- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** This is a
  design-time scaffolding tool with no runtime role. It was the sole source of
  `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 (`NU1901` ×2). Per KB 27 precedence
  ("prefer removing/replacing a legacy package over silencing the warning") it was
  removed rather than pinned.
- **Pinned `System.Security.Cryptography.Pkcs` to 10.0.13.** `Microsoft.Data.SqlClient`
  6.1.7 (via `Microsoft.EntityFrameworkCore.SqlServer`) pulled in 9.0.11, which carries
  `NU1902` (moderate) and `NU1903` (high) advisories. 10.0.13 matches the runtime major,
  per the KB rule that framework-tied packages track the target runtime.
- **Removed 44 dead `<Content Remove>` items** that pointed at hard-coded
  `C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...` paths. These were a `NETSDK1022`
  workaround for a `bootstrap` `PackageReference` that no longer exists — client-side
  libraries now come from `libman.json` into `wwwroot/lib`.

### Correctness fix — `[Required]` resolved to the wrong attribute

`ViewModels/CreateRoleViewModel.cs` imported **`Microsoft.Build.Framework`**, so its
`[Required]` bound to MSBuild's `RequiredAttribute` instead of
`System.ComponentModel.DataAnnotations.RequiredAttribute`. It compiled only because the
scaffolding package leaked MSBuild onto the compile classpath, and it performed **no
model validation at all**. Corrected to `System.ComponentModel.DataAnnotations`.

> **Behavioural change:** `CreateRoleViewModel.RoleName` is now genuinely validated as
> required, so `POST /Account/CreateRole` with an empty role name returns the view with a
> validation error instead of attempting to create a nameless role. This is the evident
> original intent (the field is annotated and `CreateRole.cshtml` renders validation
> messages), and it is required for the project to build once the scaffolding package is
> removed.

### Nullability (`Nullable=enable`) — cleared 22 warnings

`Nullable` was already enabled but the code had not been reconciled with it. Fixed at the
source rather than by suppression:

- `Attributes/ImageExtensionAttribute.cs` — `string path` → `string? path`, matching the
  `value as string` assignment that follows (`CS8600`).
- `Program.cs` — `Configuration["ConnectionStrings:DefaultConnection"]` →
  `GetConnectionString("DefaultConnection")` with a fail-fast
  `InvalidOperationException` when unset (`CS8600`). Misconfiguration now surfaces as a
  clear startup error instead of a null connection string reaching `UseSqlServer`.
- `Program.cs` — the admin role name is now a `const string adminRoleName = "admin"`
  used for both role creation and assignment, replacing the nullable
  `IdentityRole.Name` round-trip (`CS8604`).
- `Controllers/AccountController.cs` — `IdentityRole.Name` and `IdentityUser.UserName`
  are `string?` in ASP.NET Core Identity, and `FindByIdAsync` takes a non-nullable id
  while the view-model ids are `string?`. Resolved with real guards:
  `if (role?.Name is null)` on the existing not-found paths; a null-id check before
  `FindByIdAsync`; `continue` past nameless roles and unresolvable users in the
  user/role loops; `.OfType<string>()` to drop nulls from the role names passed to
  `AddToRolesAsync` (`CS8620`). The pre-existing not-found behaviour (`NotFound()` /
  returning the manager view) is preserved on every path.
- The three sites guarded by `if (ModelState.IsValid)` (login email/password, register
  password) use the null-forgiving operator with a comment, since `[Required]` already
  guarantees non-null there. This matches the existing `editModel.Users!` style.

### ASP0014 — routing

`app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))` →
top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`.
Route name and template are unchanged.

### Seeding robustness

`Program.cs` now assigns the admin role only when `userManager.CreateAsync` succeeded
(`if (result.Succeeded)`), which also consumes the previously-discarded `result`.
Previously `AddToRoleAsync` was called even after a failed user creation, where it could
only fail.

### Hosting / containerization (KB 19)

- **`Dockerfile`** targeted `sdk:6.0` / `aspnet:6.0`, which cannot host a `net10.0`
  application. Updated both stages to `10.0`. Also fixed the restore layer, which copied
  `PetShopWeb/*.csproj` to the image root and then shadowed it with `COPY . .`, leaving
  `dotnet publish` with an ambiguous project/solution choice; it now references
  `PetShopWeb/PetShopWeb.csproj` explicitly. `EXPOSE 80` / `EXPOSE 433` (the latter a
  typo for 443, with no certificate configured) were replaced with
  `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080`, the convention for the .NET 8+
  non-root ASP.NET images.
- **`docker-compose.yml`** — port mapping realigned to `3000:8080` to match the
  container's new listening port; dropped the obsolete `version:` key; added `build: .`
  so the `app` service builds the migrated application rather than relying solely on the
  stale pre-migration `yarins2000/petshopapp:1.0` image; and set
  `ConnectionStrings__DefaultConnection` to the SQL Server value the author had left
  commented out in `appsettings.json`. The active `appsettings.json` connection string
  points at `(localdb)\AspNetCoreCourse`, which is Windows-only and cannot work in a
  Linux container. No new secret was introduced — the value was already present in the
  repository as a comment.

### Incidental

`PetShopWeb.sln` was changed by the .NET Upgrade Assistant's
`SolutionProjectTypeTransformer` during the automated pass: the project type GUID moved
from the legacy C# GUID (`FAE04EC0-…`) to the SDK-style C# GUID (`9A19103F-…`), which is
the correct value for this project, and the leading BOM was dropped. Both are benign;
flagged here because the file was not edited by hand.

## Next steps (non-blocking — require a product or human decision)

1. **`ctx.Database.EnsureDeleted()` runs on every startup in every environment**
   (`Program.cs:30`). This **drops the entire database** each time the application
   starts, then recreates it and re-seeds the admin user. It is pre-existing behaviour
   and clearly a development convenience, but it is destructive if the app is ever
   started against a real database — the compose file now wires it to a SQL Server
   container, which makes that reachable. It should almost certainly be gated on
   `app.Environment.IsDevelopment()` or replaced with
   `ctx.Database.Migrate()` plus EF Core migrations. Left unchanged because removing it
   alters data-lifecycle behaviour and is a product decision, not a compile fix.
2. **Seeded admin credentials are hard-coded** (`admin@admin.com` / `Admin123`, with
   `RequireNonAlphanumeric = false`). Move to configuration/user-secrets and require a
   password change on first sign-in before any non-development deployment.
3. **The SQL Server password is in plain text** in `docker-compose.yml` (carried over
   from the commented-out `appsettings.json` value). Move to a Docker secret or an
   environment file excluded from source control.
4. **`appsettings.json` default connection string is Windows-only** (`(localdb)\…`) and
   contains a JSON comment. It is read correctly (the configuration JSON reader tolerates
   comments) but a non-Windows developer has no working default; consider moving the
   LocalDB value into `appsettings.Development.json`.
5. **`launchSettings.json` retains an `IIS Express` profile and `iisSettings`.** Harmless
   (dev-time tooling only, not part of the build) but dead for a Linux-targeted net10.0
   app; remove if Visual Studio on Windows is no longer used.
6. **`obj/` and `bin/` are tracked in the repository** and contained root-owned stale
   build artifacts that broke the first build with `MSB3374`. They are covered by
   `.gitignore` patterns but were committed previously; untracking them would prevent a
   recurrence.
7. **`docker-compose.yml` still references the pre-migration published images**
   (`yarins2000/petshopapp:1.0`, `yarins2000/petshopdb:1.0`). The app image must be
   rebuilt and republished from the updated `Dockerfile`; the `db` image was not
   inspected as part of this pass.
8. **No test project exists** in the solution, so the migration is verified by build and
   publish only. Behavioural verification of the Identity user/role management flows
   (the area with the most changes) would benefit from integration tests.
