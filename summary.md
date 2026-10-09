# PetShopWeb — .NET Framework → net10.0 Migration Summary

**Target:** `./PetShopWeb/PetShopWeb.csproj` → `net10.0`
**Result:** `dotnet build` exits **0** with **0 errors** and **0 warnings** (Debug and Release, project-level and solution-level). `dotnet publish -c Release` also succeeds clean.

---

## Starting state

The project was already SDK-style (`Microsoft.NET.Sdk.Web`), already targeting `net10.0`, and already an ASP.NET Core MVC application. The pre-build cleanup checks were verified and were already clean — the solution contains **no** references to:

- `System.Web.*`
- `System.Data.Entity`
- `System.Web.Http.*`
- `packages.config`, `Web.config`, `Global.asax`, `AssemblyInfo.cs`
- `[Index]`, `[AllowHtml]`, `[assembly: PreApplicationStartMethod]`, `CacheDependency`, `IRegisteredObject`, `HtmlTextWriter`

CodeKB confirmed the pre-upgrade sources for `AccountController` matched the working tree, i.e. no controller/startup/data-access rewrite was outstanding. The remaining work was therefore build integrity, package hygiene, and warning elimination.

## Changes made

### Build blocker
- Stale root-owned `PetShopWeb/obj` and `PetShopWeb/bin` trees from a previous run caused `error MSB3374` (`MvcApplicationPartsAssemblyInfo.cache` timestamp could not be set). Removed both; the build then succeeded.

### `PetShopWeb.csproj`
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2.** This design-time-only scaffolding package was the sole source of the two `NU1901` vulnerability advisories (`NuGet.Packaging` / `NuGet.Protocol` 6.12.1) — confirmed via `dotnet list package --include-transitive`. No code referenced it, and scaffolding is not required to build or run. Per KB 27 Step 3, migrating off the vulnerable package is preferred over tolerating the advisory. This also dropped ~30 unneeded transitive assemblies and all their localized satellite folders from the build output.
- Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items. These were absolute Windows paths from another developer's machine, referencing a `bootstrap` package the project does not even reference. They are unresolvable on Linux and served no purpose.
- Removed the redundant `<Folder Include="wwwroot\photos\" />` placeholder — that directory already holds committed content.

### Real validation bug fixed (`ViewModels/CreateRoleViewModel.cs`)
`CreateRoleViewModel` imported **`Microsoft.Build.Framework`**, so its `[Required]` resolved to `Microsoft.Build.Framework.RequiredAttribute` — an MSBuild task attribute that MVC model validation ignores entirely. Role-name validation was therefore silently inert. Corrected the import to `System.ComponentModel.DataAnnotations`.

This also compiled only because `Microsoft.Build.Framework` arrived transitively through the scaffolding package removed above; removing that package surfaced the latent bug rather than hiding it.

**Behavioral change:** `CreateRole` now actually rejects an empty role name with a validation error instead of accepting it. This is the evident intent of the attribute.

### Nullability (`Nullable=enable`) — 20 warnings cleared
Resolved on the merits rather than with `!` suppressions or `#pragma`:

- **View models** (`Login`, `Register`, `EditUser`, `EditRole`, `CreateRole`, `UserRole`, `UserRoles`): bound `string?` properties that are `[Required]` are now non-nullable `string` initialised to `string.Empty`. The explicit `[Required]` attributes are retained, so validation behaviour is unchanged (`[Required]` rejects empty strings by default, and a non-nullable bound string carries the same implicit requirement). `RegisterViewModel.ConfirmPassword` stays `string?` — it is intentionally optional and only `[Compare]`-validated. The `List<string>` collection properties (`Users`, `Claims`, `Roles`) are non-nullable and constructor-initialised, as they already were in practice.
- **Identity's genuinely nullable members** (`IdentityRole.Name`, `IdentityUser.UserName`/`Email`) cannot be re-annotated, so these are handled at the call sites in `AccountController`:
  - `role.Name` is captured into a local `string? roleName` and null-checked alongside the `role is null` check, collapsing two checks into one and feeding the subsequent `IsInRoleAsync`/`AddToRoleAsync`/`RemoveFromRoleAsync` calls a provably non-null value.
  - Display-only projections of `user.UserName` / `user.Email` use `?? string.Empty`, which preserves the exact element count and row count the views render.
  - `IdentityResult? result` in `EditUsersInRole` became `IdentityResult` — every path reaching its use assigns it.
- `Attributes/ImageExtensionAttribute.cs`: `string path` → `string? path`, since `value as string` can yield null. The existing `if (path is not null)` guard already handled it; only the declaration was wrong.

**Behavioral changes (both in states that Identity's store cannot actually produce):**
- `EditUsersInRole` (POST) now skips a posted user id that no longer resolves to a user, instead of throwing `ArgumentNullException` into a 500. Other selected users in the same post are still processed.
- `ManageUserRoles` (GET) and `EditRole` (GET) skip / reject a role whose `Name` is null rather than dereferencing it. `EditRole` returns the existing "role not found" result, consistent with its neighbouring branch.

### `Program.cs`
- Connection string now read via `builder.Configuration.GetConnectionString("DefaultConnection")` with `?? throw new InvalidOperationException(...)`, per KB 11. Previously a missing connection string was assigned to a non-nullable local and failed later with an opaque error; it now fails fast at startup with a named cause.
- Admin-role seeding uses a `const string adminRoleName = "admin"` instead of reading `adminRole.Name` back off the constructed `IdentityRole`, removing the nullable dereference at its source. Also dropped an assigned-but-unused `result` local.
- **`ASP0014`**: replaced `app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))` with the top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`. Same route, same order relative to `UseAuthentication`/`UseRouting`/`UseAuthorization`.

### Hosting (`Dockerfile`, `docker-compose.yml`) — KB 19
- Base images moved from `sdk:6.0` / `aspnet:6.0` to `sdk:10.0` / `aspnet:10.0`. The .NET 6 runtime image cannot execute a `net10.0` assembly, so this was a hard blocker for the migrated app.
- Set `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080`, matching the non-root default of the modern `aspnet` images (which no longer listen on port 80).
- Fixed a pre-existing restore-layer bug: the build stage copied `PetShopWeb/*.csproj` to `/app` root and ran a bare `dotnet restore`, then `COPY . .` placed the project in a `PetShopWeb/` subdirectory. The stage now copies the csproj to `PetShopWeb/`, restores and publishes that project explicitly. Verified by running the equivalent `dotnet publish -c Release` locally — it produces `PetShopWeb.dll` cleanly.
- `docker-compose.yml`: published port mapping changed `3000:80` → `3000:8080` to match the new container port. Removed the dead `3001:433` mapping — `433` was a typo for `443`, and no HTTPS endpoint is configured inside the container.

---

## Next steps (non-blocking, for human review)

1. **`Program.cs` calls `ctx.Database.EnsureDeleted()` then `EnsureCreated()` on every startup.** This drops and recreates the entire database — including all user accounts and roles — each time the app boots, in every environment. It was left exactly as found because changing it is a product decision, but it is unsafe outside development. Recommended: guard it with `app.Environment.IsDevelopment()`, or replace it with `ctx.Database.Migrate()` plus EF Core migrations. The `EntityFrameworkCore.Tools` package is already referenced, so adding an initial migration is straightforward.
2. **Seeded admin credentials are hard-coded** (`admin@admin.com` / `Admin123`), and `options.Password.RequireNonAlphanumeric = false` weakens the password policy globally. Move the seed credentials to configuration / user-secrets and reconsider the policy relaxation before any non-development deployment.
3. **The committed connection string in `appsettings.json` points at `(localdb)\AspNetCoreCourse`**, a Windows-only LocalDB instance, with a commented-out container connection string above it. On Linux/containers this will fail to connect. Supply it per environment via `ConnectionStrings__DefaultConnection` (the compose file is the natural place) rather than in the committed file.
4. **`wwwroot/lib` is not in the repository.** `_Layout.cshtml` and `_ValidationScriptsPartial.cshtml` reference `~/lib/bootstrap`, `~/lib/jquery`, `~/lib/jquery-validate` and `~/lib/jquery-validation-unobtrusive`, which LibMan restores from `libman.json`. Nothing in the build or the Dockerfile performs that restore, so a container image built today would serve the app without its CSS/JS and without client-side validation. Recommended: add a `libman restore` (or npm) step to the Docker build stage, or commit the `wwwroot/lib` assets. This predates the migration.
5. **`libman.json` pins `bootstrap-toggle@2.2.2`** to `wwwroot/lib/bootstrap-toggle/`, but no view references it. It can likely be dropped from the manifest.
6. **`docker-compose.yml` still references the pre-built external images** `yarins2000/petshopdb:1.0` and `yarins2000/petshopapp:1.0`. The app image is the old .NET 6 build; it needs to be rebuilt and re-pushed (or the service switched to `build: .`) for the compose stack to run the migrated application. Left as-is because the choice between a published image and a local build is a deployment decision.
7. **No test project exists** in the solution, so the migration is verified by compilation only. Adding a test project (KB 24) would let the Identity and repository behaviour above be asserted rather than reasoned about.
