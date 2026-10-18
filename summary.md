# PetShopWeb — .NET 10 (`net10.0`) Migration Summary

## Result

| Check | Status |
| --- | --- |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj` (Debug) | **Succeeded — 0 errors, 0 warnings** (exit 0) |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj -c Release` | **Succeeded — 0 errors, 0 warnings** |
| `dotnet publish -c Release` | **Succeeded — 0 errors, 0 warnings** |
| `dotnet build PetShopWeb.sln` | **Succeeded — 0 errors, 0 warnings** |
| Razor views compiled | Verified — all views emitted as `AspNetCoreGeneratedDocument.Views_*` types in `PetShopWeb.dll` |

Starting point: **27 warnings** (1 of which was a blocking `MSB3374`). End state: **0 errors, 0 warnings.**

## Starting point (important context)

Despite the task framing (".NET Framework 4.5–4.8 → net10.0"), this repository was **already an
ASP.NET Core 6.0 application** — `Microsoft.NET.Sdk.Web`, SDK-style `.csproj`, `Program.cs` minimal
hosting, `appsettings.json`, EF Core + ASP.NET Core Identity. A prior cycle had bumped the TFM
to `net10.0` and the framework-tied packages to `10.0.13`.

Consequently the KB pre-build cleanup (doc 00) and the System.Web / MVC5 / Web API 2 / EF6 /
Global.asax / Web.config / Web Forms migrations (docs 02–05, 10, 11, 16, 25, 26) had **nothing to
act on**. Verified by scan — the tree contains no `System.Web.*`, `System.Data.Entity`,
`System.Web.Http.*`, `[Index]`, `[AllowHtml]`, `PreApplicationStartMethod`, `HtmlTextWriter`,
`CacheDependency`, `IRegisteredObject`, `net6.0`/`net4x` references, and no `Global.asax`,
`Web.config` or `packages.config`. The real work was finishing the `net10.0` bump: clearing the
build blocker, the NuGet advisories, the analyzer advisory and the nullable-reference warnings.

## Changes

### 1. Build blocker — `MSB3374` (Access to the path … is denied)

`PetShopWeb/obj` and `bin` contained **root-owned** artifacts from a previous cycle. Setting a file's
last-write time requires ownership, not just write permission, so the Razor
`MvcApplicationPartsDiscovery` target failed outright. Removed both directories (gitignored build
output) so they are recreated under the build user. This was the only compilation-blocking error.

### 2. NuGet advisories — `NU1901` ×2, `NU1902`, `NU1903` (KB doc 27)

- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** This is a *design-time
  scaffolding* package; nothing in the app consumes it at build or runtime. It was the sole source of
  the vulnerable `NuGet.Packaging`/`NuGet.Protocol` 6.12.1 (`NU1901` ×2) via
  `Microsoft.DotNet.Scaffolding.Shared`. Per doc 27 precedence, removing the offending package beats
  silencing the advisory. Confirmed with `dotnet nuget why`.
- **Pinned `System.Security.Cryptography.Pkcs` to `10.0.13`.** The vulnerable `9.0.11`
  (`NU1902` moderate + `NU1903` high) also arrived transitively through
  `Microsoft.EntityFrameworkCore.SqlServer 10.0.13` → `Microsoft.Data.SqlClient 6.1.7`. No newer
  SqlServer/SqlClient release fixes the pin, so a direct top-level reference was added — doc 27
  Step 2, "a direct reference wins over a transitive one", at the target runtime major.

After the clean rebuild, no `NuGet.*` or `Pkcs` assemblies land in the output at all.

### 3. Real bug: role-name validation never ran

`ViewModels/CreateRoleViewModel.cs` imported **`Microsoft.Build.Framework`** for its `[Required]`
attribute. That resolves to `Microsoft.Build.Framework.RequiredAttribute` — an *MSBuild task*
attribute — not `System.ComponentModel.DataAnnotations.RequiredAttribute`. MVC validation ignored it
entirely, so `CreateRole` accepted a blank role name (`ModelState.IsValid` was always true for that
field). Corrected to `System.ComponentModel.DataAnnotations`.

> **Behavioural change:** `POST /Account/CreateRole` now rejects an empty `RoleName` with a
> validation error instead of attempting to create a nameless role. This is the clearly intended
> behaviour, but it is a live behaviour change worth noting.

This import was also the only consumer of `Microsoft.Build.Framework`, which the removed scaffolding
package had been supplying — so the fix was a prerequisite for change #2.

### 4. Analyzer advisory — `ASP0014`

`Program.cs` used the ASP.NET Core 3.x-era `app.UseEndpoints(e => e.MapControllerRoute(...))`.
Replaced with the top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`.
Routing behaviour is identical (`UseRouting` is already called earlier, and top-level registration
appends to the same endpoint-routing pipeline).

### 5. Nullable-reference warnings — `CS8600` ×2, `CS8604` ×12, `CS8620` ×1

The project has `<Nullable>enable</Nullable>`; these were genuine unguarded-null paths, fixed at the
source rather than suppressed. No `!` null-forgiving operators were added.

- **`Program.cs`** — `builder.Configuration["ConnectionStrings:DefaultConnection"]` (returns `string?`)
  → `GetConnectionString("DefaultConnection")` with a fail-fast `?? throw new InvalidOperationException(...)`.
  Previously a missing connection string produced a confusing null-deref later in startup.
- **`Program.cs`** — admin-role seeding passed the nullable `IdentityRole.Name` to `AddToRoleAsync`.
  Introduced a `const string adminRoleName = "admin"` used for both the role construction and the
  assignment. No behaviour change. Also dropped an unused `result` local.
- **`Attributes/ImageExtensionAttribute.cs`** — `string path` assigned from `value as string`
  → declared `string? path`; the existing `if (path is not null)` guard already handled it.
- **`Controllers/AccountController.cs`** — guarded the nullable `Id`/`UserId`/`role.Name`/`user.UserName`
  values that were being passed straight into `UserManager`/`RoleManager` APIs:
  - `EditUser`/`EditRole` (POST): a null `model.Id` now short-circuits to the existing
    `NotFound()`/`RolesManager` path instead of throwing `ArgumentNullException` (HTTP 500) inside
    `FindByIdAsync`.
  - `EditRole` (GET), `EditUsersInRole` (GET/POST): the existing `role is null` check widened to
    `role?.Name is null`, which feeds the same `NotFound()` path.
  - `EditUsersInRole` (POST): a user id that resolves to no user is now skipped via the loop's
    existing `continue` pattern rather than throwing a 500.
  - `ManageUserRoles` (GET): roles with a null `Name` are skipped; the view model is now built in a
    single object initializer.
  - `ManageUserRoles` (POST): `Select(u => u.RoleName)` yielded `IEnumerable<string?>` for
    `AddToRolesAsync` → `.OfType<string>()` filters nulls and gives `IEnumerable<string>`.
  - `LoginViewModel.Email`/`.Password` and `RegisterViewModel.Email`/`.Password` made non-nullable
    (`= string.Empty`). All four already carry `[Required]`, so validation is unchanged — the explicit
    attribute's behaviour takes precedence over MVC's implicit non-nullable requirement.
    `RegisterViewModel.ConfirmPassword` was deliberately **left nullable**: it has no `[Required]`, so
    making it non-nullable would have silently added a new required-field validation.

> **Behavioural change:** several `AccountController` edge cases that previously returned HTTP 500
> (`ArgumentNullException` from Identity) now return HTTP 404 / re-render, via the null-handling paths
> those methods already had for the adjacent cases.

### 6. Project-file cleanup

Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\..." />` items. These
were absolute paths on the original author's Windows machine, suppressing duplicate content from a
`bootstrap` NuGet package that is **no longer referenced** (Bootstrap now ships via `libman.json`
into `wwwroot/lib/bootstrap`). They were inert on Linux and produced no `NETSDK1022`. Restored the
blank-line formatting and the trailing newline the previous cycle had stripped.

### 7. Container/hosting alignment (KB doc 19)

- **`Dockerfile`** still pinned `sdk:6.0` / `aspnet:6.0`, which cannot build or run a `net10.0`
  app — updated both to `10.0`.
- Fixed a pre-existing layering bug: the build stage did `COPY PetShopWeb/*.csproj .` into `/app`
  and then `COPY . .`, leaving `PetShopWeb.csproj`, `PetShopWeb.sln` *and* `PetShopWeb/` all in the
  same directory, so the bare `dotnet publish` was ambiguous. Both `restore` and `publish` now name
  the project explicitly, preserving the original restore-layer-caching intent.
- Switched to the .NET 8+ container convention `ASPNETCORE_URLS=http://+:8080` / `EXPOSE 8080`
  (the default container port moved from 80 to 8080, and the runtime image no longer runs as root).
  `docker-compose.yml` was updated in step with it (`3000:8080`), and the `"3001:433"` mapping was
  dropped — `433` was a typo for `443`, and no HTTPS certificate is configured in the container.

The container build itself was **not executed** (no Docker daemon in this environment), so the
Dockerfile changes are verified by inspection against KB doc 19, not by a container build.

### 8. Unused usings

Removed `using System.Data;` (`AccountController`), `using System.Security;` (`EditRoleViewModel`)
and `using System.Security.Claims;` (`AccountRepository`, `IAccountRepository`).

## Not changed, deliberately

- **`Microsoft.EntityFrameworkCore.Tools`** keeps `IncludeAssets` including `runtime`, which copies
  `Microsoft.EntityFrameworkCore.Design` and a Roslyn set (`Microsoft.CodeAnalysis.*`,
  `Microsoft.Build.Framework`) into the output. This is the *official* EF Core Tools
  `PackageReference` template and emits no warning, so it was left as-is rather than deviated from.
- **`Properties/launchSettings.json`** retains its `IIS Express` profile. Dead on Linux, but it is
  Windows-developer convenience config with no build impact.

## Next steps (non-blocking — for human review)

1. **`Program.cs` calls `ctx.Database.EnsureDeleted()` on every startup.** This *drops the entire
   database* each time the app boots — all users, roles, animals and comments. It is almost
   certainly a leftover dev convenience and is destructive in test/prod. Left untouched because
   removing it changes application semantics well beyond this build/warning pass, and the seeding
   block depends on the drop-and-recreate cycle. **Recommended:** guard the whole seeding block with
   `if (app.Environment.IsDevelopment())`, and move schema management to EF Core migrations
   (`dotnet ef migrations add`, `ctx.Database.Migrate()`) for non-dev environments.
2. **Hard-coded admin credentials.** The seeding block creates `admin@admin.com` / `Admin123`,
   in source. Should move to user-secrets / environment configuration before any real deployment.
   Note `options.Password.RequireNonAlphanumeric = false` is also set globally to let that password pass.
3. **Dead seeding condition.** `if (!ctx.Users.Any(u => u.UserName == "Admin"))` checks for `"Admin"`,
   but the user created is `"admin@admin.com"` — the condition can never match an existing seeded
   admin. Harmless only because of the `EnsureDeleted()` in item 1; it becomes a duplicate-user bug
   the moment item 1 is fixed. Pre-existing; not touched as it is a logic decision.
4. **`wwwroot/photos/` does not exist**, yet the seeded `Animal.ImagePath` values all point at
   `~/photos/*.jpg` and `AdministratorController` writes new uploads to that prefix. All seeded
   animal images will 404. The `<Folder Include="wwwroot\photos\" />` item is a Visual Studio
   design-time hint only and does not create the directory at build or publish time. Needs either
   the image assets committed or directory creation at startup.
5. **`appsettings.json` has a LocalDB connection string** (`Server=(localdb)\AspNetCoreCourse`),
   which cannot work on Linux/containers, plus a commented-out container connection string with an
   inline SA password. Should be moved to environment configuration
   (`ConnectionStrings__DefaultConnection`) per KB doc 19 Step 4. The new fail-fast in `Program.cs`
   will now surface a missing value clearly at startup.
6. **No test project exists** in this repository, so "build and test" could only be validated via
   `build`/`publish`. Adding coverage for `AccountController`'s role/user management would be
   valuable given the null-path behaviour changes in item 5 of Changes. See KB doc 24 for the test
   stack.
7. **`docker-compose.yml` references prebuilt images** (`yarins2000/petshopapp:1.0`) rather than
   `build: .`, so the updated Dockerfile only takes effect once that image is rebuilt and
   re-published under a new tag.
