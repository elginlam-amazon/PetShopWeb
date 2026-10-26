# PetShopWeb — .NET 10 (`net10.0`) migration summary

Target: `./PetShopWeb/PetShopWeb.csproj` → `net10.0`

**Result: `dotnet build` succeeds with 0 errors and 0 warnings** (verified on a clean
`obj`/`bin` in both `Debug` and `Release`, at project and solution level).

## Starting state

The project was already SDK-style (`Microsoft.NET.Sdk.Web`), already targeting
`net10.0`, and already structurally ASP.NET Core — `Program.cs` with top-level
statements, `wwwroot/`, `appsettings.json`, Razor views with `_ViewImports.cshtml`.

Pre-build cleanup (KB 00) found **no** legacy surface to remove: no `System.Web.*`,
no `System.Data.Entity`, no `System.Web.Http.*`, no `Web.config`, no `Global.asax`,
no `[Index]` / `[AllowHtml]` / `[assembly: PreApplicationStartMethod]`, and no
obsolete `CacheDependency` / `IRegisteredObject` / `HtmlTextWriter` usage. Data
access is already EF Core 10 with ASP.NET Core Identity. The one legacy-namespace
hit is described under "Correctness fixes" below.

The initial build failed with a single non-code error — `MSB3374`, caused by
root-owned `obj/` intermediates left by an earlier build that the current user
could not re-stamp. Clearing `obj/` and `bin/` resolved it; restore from the
private feed (`private497`) succeeded and `Vutk.LiveCheck.Private` 1.0.0 resolved.

## Changes

### Project file (`PetShopWeb.csproj`)

- Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\…" />`
  items. They pointed at an absolute Windows path from a developer machine for a
  `bootstrap` NuGet package the project no longer references (Bootstrap now comes
  from `libman.json` into `wwwroot/lib/`), so they matched nothing on any machine.
- Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2. This is a
  design-time `aspnet-codegenerator` scaffolding tool, not an app dependency; it was
  the sole source of the vulnerable `NuGet.Packaging` / `NuGet.Protocol` 6.12.1
  (`NU1901` ×2, GHSA-g4vj-cjjj-v7hg). Per KB 27 Step 1/Step 3, removal beats
  tolerating the advisory. Note `PrivateAssets="all"` would *not* have silenced
  these — restore reports advisories regardless of asset flow.
- Added a direct pin `System.Security.Cryptography.Pkcs` **10.0.14**. Version
  9.0.11 was arriving transitively via `Microsoft.EntityFrameworkCore.SqlServer`
  → `Microsoft.Data.SqlClient` 6.1.7 and carried `NU1902` (GHSA-2fmv-rv2c-59x7)
  plus `NU1903` (GHSA-846f-f764-76mx, high). A direct reference wins over a
  transitive one (KB 27 Step 2), and 10.0.14 matches the runtime major and the
  other framework-tied packages in the project.

### Correctness fixes

- `ViewModels/CreateRoleViewModel.cs` — `using Microsoft.Build.Framework;` replaced
  with `using System.ComponentModel.DataAnnotations;`.
  **Behavioural change, intentional:** `[Required]` was binding to MSBuild's
  `Microsoft.Build.Framework.RequiredAttribute` (which marks MSBuild *task*
  parameters) rather than the DataAnnotations attribute, so role-name validation on
  the Create Role form was silently inert. It now validates, consistent with every
  other view model. This also had to be fixed regardless: `Microsoft.Build.Framework`
  was only on the compile closure transitively through the scaffolding package
  removed above.
- `Program.cs` — `builder.Configuration["ConnectionStrings:DefaultConnection"]`
  replaced with `GetConnectionString("DefaultConnection")` plus a throwing
  null-coalesce. Previously a missing connection string flowed a `null` into
  `UseSqlServer` and failed later with an opaque error; it now fails fast at
  startup with a named message.
- `Controllers/AccountController.cs` — three actions passed a possibly-`null`
  id straight into Identity (`FindByIdAsync`), which throws
  `ArgumentNullException` rather than returning `null`. `EditUser(POST)`,
  `EditRole(POST)` and `EditUsersInRole(POST)` now treat an absent id as
  "not found" and take the existing `NotFound()` / `continue` path, so a malformed
  post returns a 404 instead of a 500.
- `Controllers/AccountController.cs` — `EditRole(GET)` no longer adds a `null`
  `UserName` into the `List<string>` rendered by the view, and
  `ManageUserRoles(POST)` filters nulls out of the selected-role list with
  `.OfType<string>()` before `AddToRolesAsync`. Roles whose `Name` is `null` are
  skipped rather than passed to `IsInRoleAsync`.

### Nullable-reference warnings (`CS8600`, `CS8604`, `CS8620`)

All 22 resolved. Where a guard already made the value safe the fix is a local
non-nullable capture after the null check (e.g. `var roleName = role?.Name;`)
rather than a blanket `!`, so the analysis stays honest. The null-forgiving
operator is used in exactly two places — `Login` and `Register`, where the member
is `[Required]` and access is already inside `if (ModelState.IsValid)` — each with
a comment stating that invariant. `ImageExtensionAttribute.IsValid` declares its
local as `string?`, matching the `path is not null` check immediately below it.

### `ASP0014`

`Program.cs` — `app.UseEndpoints(e => e.MapControllerRoute(…))` replaced with the
top-level `app.MapControllerRoute(…)`. Equivalent routing; this is the form the
analyzer and modern ASP.NET Core templates expect.

### `Dockerfile`

- Base images `sdk:6.0` / `aspnet:6.0` → `sdk:10.0` / `aspnet:10.0`, matching the
  target framework (KB 19).
- Fixed the restore layer. It copied `PetShopWeb/*.csproj` to the *root* of the
  working directory and then `COPY . .` on top, leaving both a stray
  `PetShopWeb.csproj` and `PetShopWeb.sln` beside the real project directory —
  an argument-less `dotnet publish` in that directory is ambiguous. Both commands
  now name the project explicitly, which also preserves correct layer caching.
- Added `ENV ASPNETCORE_URLS=http://+:80`. .NET 8+ container images changed the
  default listening port to 8080; pinning to 80 keeps the `"3000:80"` mapping in
  `docker-compose.yml` working unchanged.
- Dropped `EXPOSE 433`. `433` is a typo for `443`, and no HTTPS listener was ever
  configured in the container, so it was documentation-only metadata for a port
  nothing bound. `EXPOSE` does not affect published ports, so this changes no
  behaviour.

## Next steps (non-blocking)

These need a product or human decision and were deliberately left alone:

1. **`docker-compose.yml` maps `3001:433`.** The app serves HTTP only inside the
   container, so this mapping has never carried traffic. Either drop it or
   terminate TLS properly (reverse proxy, or `ASPNETCORE_URLS` with a mounted
   certificate) — a deployment decision, not a code one.
2. **`Program.cs` calls `ctx.Database.EnsureDeleted()` then `EnsureCreated()` on
   every startup**, unconditionally — so every restart drops the database and
   reseeds it. That is viable for dev, destructive for test/prod. Recommended:
   guard it with `app.Environment.IsDevelopment()` and use EF Core migrations
   (`ctx.Database.Migrate()`) elsewhere. Left as-is because changing it alters
   the app's current startup contract and the project has no migrations yet.
3. **Seeded admin credentials are hard-coded** (`admin@admin.com` / `Admin123`),
   and `options.Password.RequireNonAlphanumeric = false` weakens the password
   policy globally. Move the seed credentials to configuration/secrets and
   restrict seeding to development.
4. **`docker-compose.yml` references prebuilt images** (`yarins2000/petshopapp:1.0`)
   rather than building from the updated `Dockerfile`, so the .NET 10 Dockerfile
   changes will not reach that deployment until the image is rebuilt and pushed,
   or the service is switched to `build: .`.
5. **No test project exists** in the solution, so there is no automated check that
   behaviour was preserved. The behavioural fixes above (role-name validation now
   active; 404 instead of 500 on malformed posts) are the ones worth covering
   first if a test project is added.
