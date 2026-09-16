# PetShopWeb — .NET 10 (`net10.0`) Migration Summary

**Result:** `dotnet build` succeeds with **0 errors and 0 warnings** for both `Debug` and
`Release`, at project level (`PetShopWeb/PetShopWeb.csproj`) and solution level
(`PetShopWeb.sln`). `dotnet publish -c Release` also succeeds and emits `wwwroot`
(`lib`, `photos`, `scripts`, `styles`) into the publish output.

## Starting state

The project arrived already converted to an SDK-style `Microsoft.NET.Sdk.Web` project
targeting `net10.0`, with `Nullable` and `ImplicitUsings` enabled. Pre-build cleanup
(KB `00`) was verified rather than re-applied — a repository-wide search found **no**
references to `System.Web.*`, `System.Data.Entity`, `System.Web.Http.*`, `Global.asax`,
`Web.config`, `packages.config`, `[Index]`, `[AllowHtml]`,
`[assembly: PreApplicationStartMethod]`, `HtmlTextWriter`, `CacheDependency`, or
`IRegisteredObject`. Data access is already EF Core (`IdentityDbContext`, EF Core 10.0.12)
and hosting is already minimal-hosting `Program.cs`, so KB `02`, `10`, `11`, and `16`
had nothing left to transform.

The initial build had **0 errors and 23 warnings**. All 23 are now resolved.

## Changes applied

### `PetShopWeb.csproj`

- Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2. This is a
  design-time scaffolding tool, not a build or runtime dependency, and it was the sole
  source of the two `NU1901` advisories (transitive `NuGet.Packaging` / `NuGet.Protocol`
  6.12.1). Per KB `27` the preferred fix is removal over silencing. Scaffolding can still
  be run on demand with `dotnet tool install -g dotnet-aspnet-codegenerator`.
- Deleted 44 stale `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\..." />` items.
  These were absolute Windows paths from the original developer machine — dead weight on a
  Linux build and meaningless outside that one machine. Bootstrap is delivered through
  `libman.json` into `wwwroot/lib`, so nothing depended on them.
- Dropped the empty `<Folder Include="wwwroot\photos\" />` design-time marker;
  `wwwroot/photos` contains real files and is picked up by the SDK glob.

### `ViewModels/CreateRoleViewModel.cs` — behavioural fix

`RoleName` was annotated `[Required]` while the file imported
**`Microsoft.Build.Framework`**, so it bound to MSBuild's task-parameter
`RequiredAttribute` instead of the validation attribute. The annotation was inert:
`ModelState.IsValid` never enforced it and a blank role name passed validation. Changed
the import to `System.ComponentModel.DataAnnotations`.

**This is an intentional behavioural change**: creating a role with an empty name is now
rejected with a validation error, which is what the annotation always intended. It also
removed a hidden coupling — the type only resolved because the scaffolding package
happened to drag `Microsoft.Build.Framework` onto the compile references.

### `Program.cs`

- `ASP0014`: replaced the `app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))`
  wrapper with top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`.
  Same route, same pipeline position.
- `CS8600`: `builder.Configuration["ConnectionStrings:DefaultConnection"]` assigned a
  nullable `string?` to a non-nullable local. Now uses
  `GetConnectionString("DefaultConnection")` with an explicit
  `?? throw new InvalidOperationException(...)`. Previously a missing connection string
  produced an opaque failure deeper in `UseSqlServer`; it now fails at startup with a
  named cause. Behaviour is unchanged when the setting is present.
- `CS8604`: `adminRole.Name!` — non-null by construction from `new IdentityRole("admin")`.

### `Controllers/AccountController.cs` (13 nullability warnings)

- `Login` / `Register`: `loginmodel.Email!`, `loginmodel.Password!`,
  `registermodel.Password!`. These are `[Required]` and guarded by `ModelState.IsValid`.
- `EditUser(POST)` and `EditRole(POST)`: a posted model with a null `Id` was passed
  straight into `FindByIdAsync`. Now short-circuits to the existing
  "not found" branch (`ViewBag.ErrorMessage` + `NotFound()` / `View(nameof(RolesManager))`)
  instead of relying on the framework's null handling.
- `EditUsersInRole(POST)`: `FindByIdAsync` can legitimately return `null` when a posted row
  refers to a user deleted since the page rendered. That `null` was previously passed to
  `IsInRoleAsync` / `AddToRoleAsync`. Such rows are now skipped. This prevents a
  `NullReferenceException` on a real (if uncommon) concurrent-edit path; no valid row is
  treated differently.
- Role names from `RoleManager` / `UserManager` are hoisted into a local `roleName`
  (`role.Name!`) — non-null for any role that was successfully looked up.
- `EditRole(GET)`: only adds `user.UserName` to the view model when it is non-null,
  avoiding a null entry in a `List<string>`.
- `ManageUserRoles(POST)` (`CS8620`): `AddToRolesAsync` received `IEnumerable<string?>`.
  Now filters `x.RoleName is not null` before selecting, so no null role name reaches
  Identity.

### `Attributes/ImageExtensionAttribute.cs`

`CS8600`: `string path` received `value as string`, which is null whenever the value is
neither an `IFormFile` nor a string. Declared `string? path`. The existing
`if (path is not null)` guard already handled the null case, so behaviour is identical —
only the declared type was wrong.

### `Dockerfile`

Still pinned to .NET 6 (`sdk:6.0` / `aspnet:6.0`), which cannot build or run a `net10.0`
assembly — the container build would have failed even though the app compiled. Per KB `19`
and the README target rule, base images now match the target runtime (`sdk:10.0` /
`aspnet:10.0`). Also:

- Restore now runs against the copied `.csproj` in its own directory layer, so the restore
  layer is cached properly instead of being clobbered by the subsequent `COPY . .`.
- Added `ENV ASPNETCORE_HTTP_PORTS=80`. .NET 8+ images default to **8080**, so without this
  the existing `docker-compose.yml` mapping `3000:80` would have silently stopped working
  and the README's `localhost:3000` instruction would break.
- Removed `EXPOSE 433` — a typo for 443 that never had a listener behind it (no HTTPS is
  configured in the container).

## Verification

| Command | Result |
|---|---|
| `dotnet build ./PetShopWeb/PetShopWeb.csproj` | 0 errors, 0 warnings |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj -c Release` | 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln -c Release` | 0 errors, 0 warnings |
| `dotnet publish ./PetShopWeb/PetShopWeb.csproj -c Release` | Succeeds; `wwwroot` present in output |

Builds were run after deleting `obj/` and `bin/` so that compiler warnings were re-emitted
rather than suppressed by incremental build. The application was not executed, per
instructions.

## Next steps (non-blocking — for human review)

1. **`ctx.Database.EnsureDeleted()` runs on every startup** (`Program.cs`). This
   **drops and recreates the entire database each time the app boots**, including all
   users, roles, animals, and comments. It is pre-existing behaviour and outside the scope
   of a framework migration, so it was deliberately left untouched — but it is a data-loss
   hazard in any shared or production environment. Recommendation: gate it behind
   `app.Environment.IsDevelopment()`, or replace `EnsureDeleted`/`EnsureCreated` with EF
   Core migrations (`dotnet ef migrations add InitialCreate` + `Database.Migrate()`), which
   also gives a real schema-evolution path. This needs a product decision about whether any
   deployed data must survive a restart.
2. **No test project exists** in the solution, so `dotnet test` has nothing to run and the
   `CreateRoleViewModel` validation fix and the null-row skip in `EditUsersInRole` are
   unverified by automated tests. Adding a test project is a structural addition beyond the
   migration scope; recommended follow-up is an xUnit project (KB `24`) covering the
   Identity-facing branches of `AccountController`.
3. **Connection string is Windows-only.** `appsettings.json` points at
   `Server=(localdb)\AspNetCoreCourse`, which does not exist on Linux or in a container;
   the container-appropriate `Server=tcp:db,1433` value sits commented out directly above
   it. Left as-is because choosing the default is an environment decision. Recommendation:
   keep the container value out of the committed file entirely and supply it per
   environment via `ConnectionStrings__DefaultConnection` (see KB `19` Step 4), which also
   gets the `sa` password out of source control.
4. **Credentials in source control.** `appsettings.json` contains a SQL password in the
   commented connection string, and `Program.cs` seeds a hard-coded admin
   (`admin@admin.com` / `Admin123`, also published in `README.md`) with
   `RequireNonAlphanumeric = false`. Move these to environment variables or a secret store
   and force a password change on first sign-in before any non-local deployment.
5. **`docker-compose.yml` runs prebuilt Docker Hub images** (`yarins2000/petshopapp:1.0`,
   `yarins2000/petshopdb:1.0`) rather than building from the updated `Dockerfile`, so it
   still starts the old .NET 6 image and will not pick up this migration. It also still
   maps the stale `3001:433`. Point the `app` service at `build: .` (and republish the
   image) to actually run the migrated app.
6. **Optional .NET 10 static-asset enhancement.** KB `09` suggests `MapStaticAssets` over
   `UseStaticFiles` on .NET 10 for fingerprinting plus automatic brotli/gzip
   precompression. Not applied: `MapStaticAssets` serves only assets known at build time,
   and `wwwroot/photos` is the target of animal-image paths, so switching would need
   confirmation that no images are ever added to that folder after publish. `UseStaticFiles`
   is behaviour-preserving and was kept.
7. **`libman.json` / `package.json` client-side assets** were not touched. Bootstrap 5.2.2
   and the other `wwwroot/lib` contents are several versions behind; refreshing them is a
   front-end decision independent of the framework migration.
