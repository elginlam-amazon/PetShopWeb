# PetShopWeb — .NET 10 Migration: Technical Change Log

**Project:** `PetShopWeb/PetShopWeb.csproj` (ASP.NET Core MVC + EF Core + ASP.NET Core Identity)
**Target:** `net10.0`
**Result:** `dotnet build` succeeds with **0 errors, 0 warnings** in Debug *and* Release; `dotnet publish -c Release` succeeds.

---

## 1. Starting state

The project arrived already SDK-style and already retargeted to `net10.0` by the Upgrade Assistant, with the
EF Core / Identity package family at `10.0.13`. CodeKB (space `e97c6ade…`, 1,431 indexed components) was used
to map the pre-upgrade code before editing: the legacy surface was already ASP.NET Core-shaped — constructor
injection, `IActionResult`, `IWebHostEnvironment`, `UserManager`/`SignInManager`/`RoleManager`, an
`IdentityDbContext` with `OnModelCreating` seeding, and repository interfaces behind the controllers.

Pre-build cleanup (KB 00) therefore found **nothing to unwind**: the solution contains no `System.Web.*`,
no `System.Data.Entity`, no `System.Web.Http.*`, no `Global.asax`, no `Web.config`/`packages.config`,
no `[Index]`/`[AllowHtml]`/`[PreApplicationStartMethod]`, and no obsolete `CacheDependency` /
`IRegisteredObject` / `HtmlTextWriter` usage. Verified by search before the first build.

Work this cycle concentrated on: an initial build failure, package/vulnerability hygiene,
nullable-reference correctness, two latent defects, and container modernisation.

---

## 2. Build failure fixed first

The first `dotnet build` failed with a single non-code error:

```
error MSB3374: The last access/last write time on file
"obj/Debug/net10.0/PetShopWeb.MvcApplicationPartsAssemblyInfo.cache" cannot be set.
Access to the path '...' is denied.
```

Stale `obj/` and `bin/` artifacts from a previous run were owned by `root` while the build runs as `claude`.
Mode `666` permits writing but *not* re-stamping timestamps, which requires ownership. Cleared the two
generated output directories; the build then recreated them under the correct owner. No source or project
change was involved.

---

## 3. Project file (`PetShopWeb.csproj`)

- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** Design-time scaffolding only,
  referenced by no application code, and the sole origin of 3 of the 4 security advisories. Scaffolding on
  modern .NET is provided by the `dotnet-aspnet-codegenerator` global tool, so nothing is lost at build or
  runtime. (KB 27 Step 1/3.)
- **Pinned `System.Security.Cryptography.Pkcs` to `10.0.13`** as a direct reference. `Microsoft.Data.SqlClient`
  6.1.7 (via `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13) drags in the vulnerable `9.0.11`; a direct
  top-level reference wins over a transitive one (KB 27 Step 2), and `10.0.13` matches the target runtime
  major per the KB's framework-tied-package rule. Confirmed with `dotnet nuget why` that **every** path now
  resolves to `10.0.13`. Preferred over bumping `Microsoft.Data.SqlClient` to a new major (7.x), which is a
  larger change than the problem warrants and is not the version EF Core 10.0.13 ships against.
- **Deleted 44 dead `<Content Remove>` items** hard-coded to `C:\Users\PC\.nuget\packages\bootstrap\5.2.2\…`.
  These were Visual Studio artefacts from a `bootstrap` NuGet package that is no longer referenced; the client
  libraries now come from `libman.json` into `wwwroot/lib/`. The paths cannot exist on a Linux build agent.

### Advisories cleared

| Code | Severity | Package | Resolution |
|---|---|---|---|
| `NU1903` | high | `System.Security.Cryptography.Pkcs` 9.0.11 (GHSA-846f-f764-76mx) | pinned to 10.0.13 |
| `NU1902` | moderate | `System.Security.Cryptography.Pkcs` 9.0.11 (GHSA-2fmv-rv2c-59x7) | pinned to 10.0.13 |
| `NU1901` ×2 | low | `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 (GHSA-g4vj-cjjj-v7hg) | scaffolding package removed |

`dotnet list package --vulnerable --include-transitive` and `--deprecated --include-transitive` both report none.

---

## 4. Latent defects found and fixed

Two real bugs surfaced during the migration. Both are behavioural changes and are called out here deliberately.

- **`CreateRoleViewModel` — validation was silently inert.** The file imported `Microsoft.Build.Framework`,
  so `[Required]` bound to MSBuild's *task-parameter* attribute rather than
  `System.ComponentModel.DataAnnotations.RequiredAttribute`. It is not a `ValidationAttribute`, so MVC ignored
  it and **role name was never validated**. It only compiled because that assembly arrived transitively through
  the scaffolding package — removing the package exposed it. Corrected the import to
  `System.ComponentModel.DataAnnotations`. *Effect: creating a role with an empty name is now rejected.*

- **`AccountController.EditUsersInRole` (POST) — unguarded null.** The `IdentityUser?` returned by
  `FindByIdAsync` was passed straight into `IsInRoleAsync` / `AddToRoleAsync` / `RemoveFromRoleAsync`, so a
  posted unknown or tampered user id threw `ArgumentNullException` and crashed the request. Unknown ids are now
  skipped (`continue`), consistent with how the surrounding code already handles a missing role.
  *Effect: the endpoint completes instead of erroring on an unrecognised id.*

A matching guard was applied to `EditUser` (POST) and `EditRole` (POST), where a null `model.Id` / `editModel.Id`
would likewise have thrown. These now fall into the existing "not found" branch (`NotFound()` / the
`RolesManager` view) rather than raising an exception.

---

## 5. `Program.cs` — hosting patterns

- `app.UseEndpoints(e => e.MapControllerRoute(...))` → **top-level `app.MapControllerRoute(...)`**, clearing
  analyser warning `ASP0014`. The route template is unchanged.
- Connection string now read with `Configuration.GetConnectionString("DefaultConnection")` plus
  `?? throw new InvalidOperationException(...)`. Previously `Configuration["ConnectionStrings:DefaultConnection"]`
  returned `string?` (`CS8600`) and a missing value would have been handed to EF Core as `null`; startup now
  fails fast with a clear message.
- Seeded admin role name extracted to a `const` and used for `AddToRoleAsync`, replacing the nullable
  `adminRole.Name` (`CS8604`). Same role name (`"admin"`), same behaviour.

---

## 6. Nullable-reference correctness — 22 warnings eliminated at source

`<Nullable>enable</Nullable>` was already set. All remaining `CS8600` / `CS8604` / `CS8620` warnings were
resolved at the point of use. **No warning was suppressed** via `#pragma`, `NoWarn`, or by weakening the
nullable setting.

The approach distinguished two cases:

- **Guaranteed non-null by a framework invariant** → null-forgiving operator, which preserves the existing
  exception contract rather than silently substituting `""`:
  - `loginmodel.Email` / `.Password` and `registermodel.Password` are `[Required]` and read only inside
    `if (ModelState.IsValid)`, so validation has already rejected null.
  - `IdentityRole.Name` and `IdentityUser.UserName` for entities just loaded from the store.
  - `UserRolesViewModel.RoleName` in the `AddToRolesAsync` projection (`CS8620`).
- **Null genuinely reachable and currently crashing** → a real guard. These are the `FindByIdAsync` results
  covered in §4.

`ImageExtensionAttribute.IsValid`: `path` declared `string?` so the `value as string` assignment is honest
(`CS8600`). The existing `if (path is not null)` guard already covered it — no behaviour change.

View models were intentionally **left as `string?`**. Converting them to non-nullable with `= string.Empty`
initialisers would also compile, but it changes what the binder and `[Required]` see (empty-string vs null) and
is a wider change than clearing the warnings requires.

---

## 7. Container and hosting (KB 19)

The `Dockerfile` still targeted .NET 6 and was broken by the retarget — the 6.0 SDK cannot build a `net10.0`
app, and the 6.0 runtime cannot run it.

- `sdk:6.0` / `aspnet:6.0` → **`sdk:10.0` / `aspnet:10.0`**.
- **Fixed an ambiguous publish target.** The previous `COPY PetShopWeb/*.csproj .` followed by `COPY . .` and a
  bare `dotnet publish` left a stray `.csproj` *and* `PetShopWeb.sln` *and* the project directory in the same
  working directory, which MSBuild cannot disambiguate (`MSB1011`). Both `restore` and `publish` now name the
  project explicitly, and the restore layer is preserved for Docker build caching.
- Adopted the .NET 8+ container convention: `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080`. Modern `aspnet`
  images run **non-root** and no longer bind port 80.
- `docker-compose.yml`: host mapping realigned to `3000:8080`; dropped the `3001:433` mapping (`433` was a typo
  for 443, and no certificate was ever configured). Added `build: .` to the `app` service so compose builds the
  upgraded code rather than only pulling the stale prebuilt tag — see Next steps.

---

## 8. Verification

```
dotnet build  PetShopWeb/PetShopWeb.csproj              → 0 Error(s), 0 Warning(s)
dotnet build  PetShopWeb/PetShopWeb.csproj -c Release   → 0 Error(s), 0 Warning(s)
dotnet build  PetShopWeb.sln                            → 0 Error(s), 0 Warning(s)
dotnet publish PetShopWeb/PetShopWeb.csproj -c Release  → exit 0
dotnet list package --vulnerable --include-transitive    → none
dotnet list package --deprecated  --include-transitive   → none
```

Both configurations were verified from a cleaned `obj/` + `bin/` tree, so the result does not depend on
incremental state. Per engagement policy the application was **not executed**.

**Warning burn-down:** 1 error + 8 warnings (build failing) → 27 warnings (build green) → **0**.

Files changed: 7 by this cycle (`Dockerfile`, `docker-compose.yml`, `PetShopWeb.csproj`, `Program.cs`,
`AccountController.cs`, `CreateRoleViewModel.cs`, `ImageExtensionAttribute.cs`). `PetShopWeb.sln` carries a
pre-session Upgrade Assistant edit migrating the project-type GUID to the SDK-style identifier, which is
correct and was left in place.

---

## 9. Next steps

### Validation gates (required before production release)

- **Smoke-test the running application.** The app was not executed and the solution contains **no test
  projects**, so a green build is not a functional guarantee. Exercise login/logout, registration, catalog
  browsing, animal CRUD, image upload, and role/user management.
- **Regression-test the two §4 fixes specifically:** confirm role creation now rejects an empty name, and that
  posting an unknown user id to `EditUsersInRole` returns cleanly instead of erroring.
- **Validate EF Core data behaviour against a real SQL Server instance:** lazy-loading proxies
  (`UseLazyLoadingProxies`), `HasData` seeding, and the `Microsoft.Data.SqlClient` 6.x
  **`Encrypt=true`-by-default** change (the current connection strings set `TrustServerCertificate=True`).
- **Rebuild and republish the container images.** `docker-compose.yml` still references the prebuilt
  `yarins2000/petshopapp:1.0` / `petshopdb:1.0` tags. `build: .` was added so a local `docker compose up --build`
  picks up the upgraded code, but the **published tags must be rebuilt and pushed** before the upgrade reaches
  any environment that pulls them. Then verify the new `8080` mapping end-to-end.
- **Add a minimum test suite.** The absence of any automated tests is the largest residual risk here and for
  every future upgrade.

### Recommended improvements (prioritised, not done — each needs a product decision)

- **P1 — `Program.cs` calls `ctx.Database.EnsureDeleted()` on every startup**, dropping and re-seeding the
  database on each boot. Pre-existing behaviour, deliberately left untouched as it is outside this migration's
  scope, but **destructive in test and production**. Guard with `IsDevelopment()`, or move to EF Core migrations
  (`ctx.Database.Migrate()`).
- **P1 — Hard-coded seeded admin credentials** (`admin@admin.com` / `Admin123`, alongside
  `RequireNonAlphanumeric = false`). Move to user secrets / environment variables and seed in development only.
  Note the seeding guard checks `UserName == "Admin"` but creates `admin@admin.com`, so the check never matches
  the user it creates — harmless only because `CreateAsync` then fails on the duplicate.
- **P2 — `appsettings.json` ships a Windows-only `(localdb)` connection string** that cannot resolve in a Linux
  container (the container variant sits commented out beside it). Keep dev values in
  `appsettings.Development.json` and inject per environment via `ConnectionStrings__DefaultConnection`;
  `docker-compose.yml` currently sets **no** environment variables for the `app` service, so the containerised
  app has no usable connection string.
- **P2 — Normalise middleware order.** `UseAuthentication()` currently precedes `UseRouting()`. This is
  functionally correct today (`UseAuthorization()` is correctly placed after `UseRouting()`), but it diverges
  from the documented pipeline order and is fragile under future edits.
- **P3 — Adopt `MapStaticAssets()`** in place of `UseStaticFiles()` for .NET 10 build-time fingerprinting and
  compression (requires moving views to the `@Assets[...]` helper). See KB 09/15.
- **P3 — Tighten the entity model.** `DbSet<T>?` and nullable scalars (`Animal.Name`, `Description`, …) rely
  solely on `[Required]` for validation; non-nullable types would let EF Core infer `NOT NULL` from the type
  system. Requires a migration review, so not attempted here.
- **P3 — Housekeeping:** remove the obsolete top-level `version:` key from `docker-compose.yml` (Compose V2
  warns on it); consider a CI gate enforcing `TreatWarningsAsErrors` so the warning count cannot regress.
- **P4 — Process:** the Upgrade Assistant logged `Msbuild was not found` and so could not build or validate its
  own output — it retargeted and remapped packages blind, and its output did not build. Provisioning MSBuild in
  the upgrade container would let it catch that itself instead of deferring to the downstream agent.
