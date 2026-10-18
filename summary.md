# PetShopWeb — .NET 10 (`net10.0`) Migration Summary

**Project:** `./PetShopWeb/PetShopWeb.csproj`
**Target:** `net10.0`
**Final state:** `dotnet build` exits **0** with **0 errors and 0 warnings** (Debug, Release, solution-wide, and `dotnet publish`).

---

## 1. Starting point

A scan of the working tree found **no .NET Framework surface at all**: no `System.Web.*`, no
`System.Data.Entity`, no `System.Web.Http.*`, no `Global.asax`, no `Web.config`, no
`packages.config`, and no non-SDK-style project. The project was already SDK-style
(`Microsoft.NET.Sdk.Web`) with `Program.cs` minimal hosting, `appsettings.json`, EF Core and
ASP.NET Core Identity, and its `TargetFramework` was already set to `net10.0` with the
`Microsoft.AspNetCore.*` / `Microsoft.EntityFrameworkCore.*` packages at `10.0.13`.

The legacy-migration playbooks (KB 02 System.Web, 03/04 MVC/Web API, 16 EF6→EF Core, 10
Global.asax, 11 Web.config, 25/26 Web Forms) therefore had nothing to act on. The outstanding
work was to make the `net10.0` build actually succeed and to clear everything it surfaced.

## 2. Build blocker fixed — `MSB3374`

```
error MSB3374: The last access/last write time on file
"obj/Debug/net10.0/PetShopWeb.MvcApplicationPartsAssemblyInfo.cache" cannot be set.
Access to the path '...' is denied.
```

`PetShopWeb/obj` and `PetShopWeb/bin` contained **root-owned artifacts from a previous cycle**.
Setting a file's last-write time requires *ownership*, not merely write permission, so the Razor
`MvcApplicationPartsDiscovery` target failed outright despite mode `0666`. Both directories are
gitignored build output, so they were removed and regenerate under the build user.

## 3. Project file and packages

| Change | Detail |
|---|---|
| Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2 | Design-time scaffolding package that nothing in the app consumes. It was the sole source of the vulnerable `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 (`NU1901` ×2). |
| Added `System.Security.Cryptography.Pkcs` 10.0.13 (direct pin) | The vulnerable `9.0.11` (`NU1902` moderate + `NU1903` **high**) arrives transitively via `EFCore.SqlServer` → `Microsoft.Data.SqlClient` 6.1.7. A direct top-level reference wins over the transitive one (KB 27 Step 2/3); pinned at the target runtime major per the KB framework-tied-package rule. |
| Removed 44 dead `<Content Remove=… />` items | Hard-coded to `C:\Users\PC\.nuget\packages\bootstrap\5.2.2\…` — absolute paths from the original author's Windows machine, suppressing a `bootstrap` NuGet package that is no longer referenced (Bootstrap now ships via `libman.json`). Inert on Linux. |

`dotnet list package --vulnerable --include-transitive` now reports **no vulnerable packages**.

## 4. Code changes

All fixes were applied at the source. No warnings were suppressed and no `#pragma`/`NoWarn` was added.

### Latent bug — role-name validation never ran

`ViewModels/CreateRoleViewModel.cs` imported `Microsoft.Build.Framework`, so its `[Required]`
resolved to **`Microsoft.Build.Framework.RequiredAttribute`** (an MSBuild task attribute), not
`System.ComponentModel.DataAnnotations.RequiredAttribute`. MVC validation ignored it entirely.
Changed to `using System.ComponentModel.DataAnnotations;`.

> **Behavioural change:** `POST /Account/CreateRole` now rejects a blank `RoleName` with a
> validation error instead of silently accepting it.

This was also the only consumer of `Microsoft.Build.Framework` in the codebase.

### `ASP0014` — top-level route registration

`Program.cs`: the ASP.NET Core 3.x-era `app.UseEndpoints(e => e.MapControllerRoute(…))` became
top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`.
Routing behaviour is identical.

### Nullable-reference correctness — 15 warnings (`CS8600` ×2, `CS8604` ×12, `CS8620` ×1)

- **`Program.cs`** — `Configuration["ConnectionStrings:DefaultConnection"]` →
  `GetConnectionString("DefaultConnection")` with a fail-fast
  `?? throw new InvalidOperationException(…)`. A missing connection string previously surfaced as
  a confusing null dereference later in startup.
- **`Program.cs`** — the nullable `IdentityRole.Name` flowing into `AddToRoleAsync` replaced with a
  `const string adminRoleName = "admin"` used for both role construction and assignment (no
  behaviour change). Dropped an unused `result` local.
- **`Attributes/ImageExtensionAttribute.cs`** — `string path` assigned from `value as string` →
  `string? path`; the existing `if (path is not null)` guard already covered the null case.
- **`ViewModels/LoginViewModel.cs`, `ViewModels/RegisterViewModel.cs`** — `Email` / `Password` made
  non-nullable (`= string.Empty`). All four already carry `[Required]`, so validation is unchanged.
  `ConfirmPassword` was **deliberately left nullable** — it has no `[Required]`, and making it
  non-nullable would have silently introduced a brand-new required-field validation.
- **`Controllers/AccountController.cs`** — guarded nullable `Id` / `UserId` / `role.Name` /
  `user.UserName` values that were being passed straight into `UserManager` / `RoleManager` APIs:
  - `EditUser` (POST), `EditRole` (POST) — a null `model.Id` now short-circuits into the method's
    **existing** `NotFound()` / `RolesManager` path instead of throwing `ArgumentNullException`
    (HTTP 500) inside `FindByIdAsync`.
  - `EditRole` (GET), `EditUsersInRole` (GET/POST) — the `role is null` check widened to also cover
    a null `role.Name`, feeding the same existing not-found path.
  - `EditUsersInRole` (POST) — an unresolvable user id is now skipped via the loop's existing
    `continue` pattern rather than dereferencing null.
  - `EditRole` (GET) — `user.UserName` coalesced to `string.Empty` before being added to the
    display list (renders the same as the previous null).
  - `ManageUserRoles` (GET) — roles with a null `Name` are skipped.
  - `ManageUserRoles` (POST) — `Select(u => u.RoleName)` yielded `IEnumerable<string?>` for
    `AddToRolesAsync`; narrowed with `.OfType<string>()`.

> **Behavioural change:** several `AccountController` edge cases that previously returned
> **HTTP 500** now return **HTTP 404** or re-render, using null-handling paths those same methods
> already had for adjacent cases.

### Hygiene

Removed unused usings: `System.Data` (`AccountController`), `System.Security`
(`EditRoleViewModel`), `System.Security.Claims` (`AccountRepository`, `IAccountRepository`).

## 5. Hosting / containerization (KB 19)

- **`Dockerfile`** still pinned `sdk:6.0` / `aspnet:6.0`, which cannot build or run a `net10.0`
  app. Both stages moved to `10.0`.
- Adopted the .NET 8+ container convention: `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080` (the
  default container port moved 80 → 8080 and the runtime image no longer runs as root).
- Fixed a pre-existing layering bug: the build stage copied the `.csproj` to the working-directory
  root and then did `COPY . .`, leaving a stray `PetShopWeb.csproj`, the `.sln`, and `PetShopWeb/`
  in one directory — making the bare `dotnet publish` ambiguous. `restore` and `publish` now name
  the project explicitly, preserving restore-layer caching.
- **`docker-compose.yml`** updated in step (`3000:8080`). The `"3001:433"` mapping was dropped —
  `433` was a typo for `443` and no HTTPS certificate is configured in the container. The obsolete
  `version:` key was removed (Compose v2 warns on it).

## 6. Verification performed

| Check | Result |
|---|---|
| `dotnet build ./PetShopWeb/PetShopWeb.csproj` (Debug) | **0 errors, 0 warnings — exit 0** |
| `dotnet build ./PetShopWeb/PetShopWeb.csproj -c Release` | 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln` | 0 errors, 0 warnings |
| `dotnet publish -c Release` | 0 errors, 0 warnings |
| Razor view compilation | All 23 `.cshtml` files emitted as `AspNetCoreGeneratedDocument.Views_*` types in `PetShopWeb.dll` (the Razor source generator runs inside `CoreCompile`, so view errors are build errors) |
| `dotnet list package --vulnerable --include-transitive` | No vulnerable packages |
| Legacy-API residue scan | No `System.Web.*`, `System.Data.Entity`, `System.Web.Http.*`, `net4x`/`net6.0`, `packages.config` or `Global.asax` anywhere in the tree |

---

## 7. Next steps

Non-blocking items recorded for human review. Nothing below blocks the build.

### Runtime validation (not performed — build-only verification)

- **No code was executed.** Per the toolkit's instructions the compiled app was not launched.
  Smoke-test login, registration, catalogue browsing, animal CRUD and comment posting.
- **Exercise the changed null paths.** Several `AccountController` routes now return 404 instead of
  500 — confirm `EditUser`, `EditRole`, `EditUsersInRole` and `ManageUserRoles` behave correctly
  with missing or invalid ids.
- **Verify the role-validation fix end to end.** Submitting a blank role name on `CreateRole` must
  now show a validation error.
- **Confirm EF Core 10 against a real SQL Server.** Lazy-loading proxies, Identity schema creation
  and the seed data were not executed; this bump spans several EF Core majors.
- **Build and run the container.** The Dockerfile was changed but not built (no Docker daemon in
  this environment). Validate `docker build` and that the app answers on port **8080**.

### Pre-existing defects left untouched (they change application semantics, not the build)

- **`Program.cs` calls `ctx.Database.EnsureDeleted()` on every startup**, dropping the entire
  database (all users, roles, animals, comments) on each boot. Destructive in test and prod.
  *Recommendation:* wrap the seeding block in `if (app.Environment.IsDevelopment())` and move
  schema management to EF Core migrations (`ctx.Database.Migrate()`).
- **Hard-coded admin credentials** (`admin@admin.com` / `Admin123`) are committed in source, with
  `options.Password.RequireNonAlphanumeric = false` set globally to let that password pass. Move to
  user-secrets or environment configuration.
- **Dead seeding condition** — `if (!ctx.Users.Any(u => u.UserName == "Admin"))` tests for `"Admin"`
  but creates `"admin@admin.com"`, so it can never match. Harmless *only* because of the
  `EnsureDeleted()` above; it becomes a duplicate-user bug the moment that is fixed.
- **Connection-string hygiene** — `appsettings.json` ships a LocalDB string
  (`Server=(localdb)\AspNetCoreCourse`) that cannot work on Linux, plus a commented-out container
  string with an inline SA password. Move to the `ConnectionStrings__DefaultConnection`
  environment variable (KB 19 Step 4).
- **`AdministratorController` injects `IWebHostEnvironment` but never uses it**, while
  `AddNewAnimal` stores `"~/photos/" + newAnimal.ImagePath` without ever writing the uploaded file
  to disk. Image upload looks incomplete; needs a product decision before changing.

### Improvements

- **Add a test project — the repository has none.** This was the single biggest gap in validating
  the upgrade: "build and test" could only ever be *build*. Start with `AccountController` role and
  user management, given the behavioural changes above.
- **`docker-compose.yml` references prebuilt images** (`yarins2000/petshopapp:1.0`) rather than
  `build: .`, so the updated Dockerfile only takes effect once that image is rebuilt and
  re-published under a new tag.
- **Add CI enforcement** — `<TreatWarningsAsErrors>` plus NuGet audit gating would prevent the
  nullable and advisory regressions from reappearing.
- **Consider Central Package Management** (`Directory.Packages.props`) so the next runtime-major
  bump is a single-file change.
- **`Properties/launchSettings.json` retains an `IIS Express` profile** — dead on Linux, harmless
  Windows-developer convenience. Left in place.
