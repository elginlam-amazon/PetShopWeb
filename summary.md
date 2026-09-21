# PetShopWeb — .NET Framework → `net10.0` Migration Summary

**Project:** `./PetShopWeb/PetShopWeb.csproj`
**Target framework:** `net10.0`
**Final build state:** `dotnet build` → **0 errors, 0 warnings** (Debug, Release, and via `PetShopWeb.sln`); `dotnet publish -c Release` succeeds.

---

## 1. Pre-build cleanup (KB 00)

Verified the solution contains **no** remaining legacy references before building — a repo-wide scan for
`System.Web.*`, `System.Data.Entity`, `System.Web.Http.*`, `Global.asax`, `[Index]`, `[AllowHtml]`,
`[assembly: PreApplicationStartMethod]`, `HtmlTextWriter`, `IRegisteredObject`, `CacheDependency`,
`NuGet.Core`, and `Autofac.Mvc5` returned zero hits across `*.cs`, `*.cshtml`, `*.csproj`, `*.config`.

The initial build failed with a non-code error, `MSB3374` — the checked-in `obj/` and `bin/` trees were
owned by another user, so the Razor `MvcApplicationPartsAssemblyInfo.cache` timestamp could not be
updated. Removing the stale `obj/`+`bin/` output resolved it; these are build artifacts and are already
covered by `.gitignore`.

## 2. Project file (KB 01, 27)

`PetShopWeb.csproj` was already SDK-style (`Microsoft.NET.Sdk.Web`, `net10.0`, `Nullable`/`ImplicitUsings`
enabled). Cleaned up:

- **Removed 44 stale `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items.** These
  hard-coded a Windows developer's NuGet cache path and existed to suppress `NETSDK1022` duplicate-content
  errors from a `bootstrap` `PackageReference` that no longer exists (Bootstrap now comes from LibMan into
  `wwwroot/lib/`). They are meaningless on Linux and dead weight in the build.
- **Removed `<Folder Include="wwwroot\photos\" />`** — a Windows-separator placeholder for a folder that
  already contains committed files.
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** This is a design-time-only
  scaffolding package (`dotnet aspnet-codegenerator`) that is not needed to build or run the app, and it was
  the sole source of the two `NU1901` advisories (transitive `NuGet.Packaging` / `NuGet.Protocol` 6.12.1).
  Removal is the KB-27-preferred fix (remove/replace over silencing). Re-add it as a local tooling
  reference if scaffolding is needed again.

Remaining package set, all matched to the runtime major (`10.0.x`) per the KB target rule:
`Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Proxies`,
`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Tools` (design-time,
`PrivateAssets=all`).

## 3. Application lifecycle / hosting (KB 10, 14)

`Program.cs` already used the minimal-hosting model (no `Global.asax`, no `Startup.cs`). Fixed:

- **`ASP0014`** — replaced the `app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))` wrapper
  with the top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`.
  Same route template, same pipeline position.
- **Connection string resolution** — `builder.Configuration["ConnectionStrings:DefaultConnection"]`
  (returns `string?`, producing `CS8600`) is now
  `builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(...)`.
  *Behavioural note:* a missing connection string now fails fast at startup with a clear message instead of
  silently passing `null` into `UseSqlServer`.
- **Admin seeding** — the seeded role name is now a local `const string adminRoleName = "admin"`, so
  `AddToRoleAsync` no longer receives the nullable `IdentityRole.Name` (`CS8604`). The value passed is
  identical. Also dropped an unused `result` local from `userManager.CreateAsync(...)`.

## 4. Configuration (KB 11)

Configuration was already `appsettings.json` / `appsettings.Development.json` with no `Web.config`
remnants; `ConnectionStrings:DefaultConnection` is read through the standard provider chain, so container
and CI overrides via `ConnectionStrings__DefaultConnection` work unchanged. No changes required.

## 5. Data access (KB 16)

`PetShopContext` is already EF Core (`IdentityDbContext`, `DbContextOptions`, lazy-loading proxies via
`UseLazyLoadingProxies`) with `HasData` seeding — no EF6 constructs remained. Removed two unused usings
(`Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Identity`).

## 6. Auth / Identity and MVC code (KB 03, 08)

**Real defect found and fixed:** `ViewModels/CreateRoleViewModel.cs` imported
`Microsoft.Build.Framework` — so `[Required]` bound to MSBuild's `RequiredAttribute`, **not** to
`System.ComponentModel.DataAnnotations.RequiredAttribute`. It compiled but performed no validation, so
`CreateRole` accepted an empty role name. The import is now
`System.ComponentModel.DataAnnotations`. (It was also the only thing keeping the removed
scaffolding package load-bearing.)

`AccountController` nullability (19 `CS8604`/`CS8620`/`CS8600` warnings) was resolved with real guards
rather than suppression:

| Location | Fix |
|---|---|
| `Login` POST | `LoginViewModel.Email`/`Password` are now non-nullable `string` (`= string.Empty`). Both already carry `[Required]`, so validation behaviour is unchanged. |
| `Register` POST | Same for `RegisterViewModel.Email`/`Password`. `ConfirmPassword` intentionally left nullable so its `[Compare]`-only semantics (no implicit required) are preserved. |
| `EditUser` POST, `EditRole` POST | `FindByIdAsync` is only called when the posted `Id` is non-null; a null `Id` takes the existing `NotFound()` path. This matches the previous runtime result (`FindByIdAsync(null)` resolves to no user). |
| `EditRole` GET, `EditUsersInRole` GET/POST | After the existing not-found check, the role name is hoisted into a local (`if (role?.Name is null) …; var roleName = role.Name;`) and used for all `IsInRoleAsync` / `AddToRoleAsync` / `RemoveFromRoleAsync` calls. |
| `EditRole` GET user list | `editModel.Users!.Add(user.UserName ?? string.Empty)` — a null user name previously added `null` to the list and rendered blank; it still renders blank. |
| `ManageUserRoles` GET | `IsSelected = role.Name is not null && await _userManager.IsInRoleAsync(user, role.Name)` — a null role name yields `false`, as before, and the row is still listed. |
| `ManageUserRoles` POST | `AddToRolesAsync(user, …Select(u => u.RoleName).OfType<string>())` filters out null role names instead of passing `IEnumerable<string?>`. |

**Two intentional behaviour improvements** (both in `EditUsersInRole` POST): users whose posted `UserId`
is null, or that no longer exist, are now skipped via `continue` instead of being passed as `null` into
`UserManager.IsInRoleAsync` (which threw `ArgumentNullException` → HTTP 500). The loop's `IdentityResult?`
local also became a non-nullable `IdentityResult`, since every path now assigns it before use.

## 7. Views and static files (KB 15, 09)

All 24 Razor views compile into the application assembly (the .NET 6+ default; no separate
`*.Views.dll`), and `wwwroot/` (`lib`, `photos`, `scripts`, `styles`) is published intact. Client assets
are managed by LibMan/npm rather than `System.Web.Optimization`, so no bundling migration was needed.

## 8. Containerization (KB 19)

`Dockerfile` still targeted **.NET 6**, which cannot run a `net10.0` assembly. Updated to
`mcr.microsoft.com/dotnet/sdk:10.0` / `aspnet:10.0`, and fixed the build stage, which could not have
worked as written: it copied `PetShopWeb/*.csproj` to the context root and then ran a bare
`dotnet publish` over a directory containing both a solution and two candidate project files. It now
restores and publishes `PetShopWeb/PetShopWeb.csproj` explicitly.

Because .NET 8+ images default to listening on port 8080 (the old images used 80), the runtime stage sets
`ASPNETCORE_HTTP_PORTS=80` so the existing `docker-compose.yml` mapping (`3000:80`) keeps working without
editing the compose file.

## 9. Not applicable to this solution

No Web Forms, WCF, .NET Remoting, OWIN middleware, Swagger/Swashbuckle, `System.Drawing`, custom
cryptography, `Newtonsoft.Json`, `log4net`/`NLog`, or third-party DI container is present — KB 12, 13,
20–23, 25, 26, and 28 have nothing to apply here. There is also **no test project in the solution**, so
no test-stack migration (KB 24) was possible and no unit tests could be run as verification; the build and
publish gates above are the verification performed.

---

## Next steps

Non-blocking items for human review — none of these block the build, and none were changed because each
needs a product decision:

1. **`ctx.Database.EnsureDeleted()` runs on every startup** (`Program.cs`). The database and all its data
   are dropped and recreated each time the app boots. That is acceptable for a demo but is destructive in
   test/prod. Recommended: switch to `ctx.Database.Migrate()` with proper EF Core migrations, and gate any
   destructive reset behind `app.Environment.IsDevelopment()`. Left as-is to avoid changing behaviour
   during the framework migration.
2. **Hard-coded seed admin credentials** — `admin@admin.com` / `Admin123` are literals in `Program.cs`, and
   `Password.RequireNonAlphanumeric` is disabled globally. Move the seed credentials to configuration/user
   secrets and restrict seeding to Development.
3. **Seed guard checks the wrong user name** — the admin user is created when no user named `"Admin"`
   exists, but it is created with `UserName = "admin@admin.com"`. The guard therefore never matches on
   restart. Harmless today only because `EnsureDeleted()` wipes the database first; it becomes a duplicate
   -user bug the moment item 1 is fixed.
4. **Default connection string points at Windows LocalDB** (`Server=(localdb)\AspNetCoreCourse`), which
   does not exist on Linux or in the container. The container connection string is present but commented
   out in `appsettings.json`. Decide per environment and supply it via
   `ConnectionStrings__DefaultConnection`; consider removing the commented-out literal (it contains a
   password) from source control.
5. **`docker-compose.yml` maps `3001:433`** — `433` is almost certainly a typo for `443`, and the app
   never listened on it. Left untouched as it is unrelated to the framework upgrade; remove the mapping or
   configure HTTPS properly (KB 19 §7).
6. **`app.UseAuthentication()` is called before `app.UseRouting()`.** This works and was left as-is to
   preserve existing behaviour, but the canonical ASP.NET Core order is
   `UseRouting` → `UseAuthentication` → `UseAuthorization`; worth normalising alongside any future
   pipeline change.
7. **`libman.json` references `bootstrap-toggle@2.2.2`** with no corresponding files under
   `wwwroot/lib/bootstrap-toggle/` and no matching `package.json` entry. Either restore the library or drop
   the LibMan entry.
8. **Scaffolding tooling was removed** (see §2). If `dotnet aspnet-codegenerator` is part of the team's
   workflow, re-add `Microsoft.VisualStudio.Web.CodeGeneration.Design` at a version whose transitive
   `NuGet.Packaging`/`NuGet.Protocol` are not flagged by `NU1901`, or install it as a local dotnet tool so
   it stays out of the project's restore graph.
9. **No automated tests exist.** A minimal test project covering the repositories and the `AccountController`
   role/user flows would make future upgrades verifiable beyond "it compiles"; the null-guard changes in
   §6 are the natural first candidates for coverage.
