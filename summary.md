# PetShopWeb → .NET 10 Migration Summary

**Target:** `net10.0` · **Project:** `./PetShopWeb/PetShopWeb.csproj`
**Final build state:** `dotnet build` exits **0** — **0 errors, 0 warnings** (verified from a clean `obj/`+`bin/` in both Debug and Release, and via the solution file).

---

## 1. Starting state

CodeKB (index of the pre-upgrade source, space `e97c6ade532988a473d1a5675e4ef1e8`, 1431 components) was used to map the
components before editing: `AccountController`, `AdministratorController`, `AnimalDetailsController`, `CatalogController`,
`HomeController`, `ErrorController`, `PetShopContext`, the four repositories, the view models and
`ImageExtensionAttribute`.

The project was already SDK-style and already carried the ASP.NET Core shape (`Program.cs`, `wwwroot/`,
`appsettings.json`, Identity + EF Core, tag-helper Razor views). A full scan confirmed **no legacy surface left to port**:

- no `System.Web.*`, `System.Web.Http.*` or `System.Data.Entity` references
- no `Global.asax`, no `Web.config` / `*.config`
- no `[Index]`, `[AllowHtml]`, `[assembly: PreApplicationStartMethod]`, `HtmlTextWriter`, `CacheDependency`, `IRegisteredObject`
- no `HttpContext.Current`
- no Web Forms, WCF, System.Drawing, Swashbuckle, Remoting or test projects

So the pre-build cleanup (KB 00) had nothing to remove, and the work in this cycle was the **remaining build blocker,
package integrity, and warning elimination**.

## 2. Build blocker fixed

`MSB3374` — `Access to the path '.../obj/Debug/net10.0/PetShopWeb.MvcApplicationPartsAssemblyInfo.cache' is denied`.
Stale `obj/` and `bin/` trees were checked in root-owned; the Razor targets could not stamp the timestamp on a file the
build user does not own. Removed both directories; they are build output and are regenerated.

## 3. Project file (KB 01, 06, 27)

- Removed **44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items** — absolute
  Windows paths from the original author's machine, meaningless on any other host and unusable on Linux.
- Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` (10.0.2). Design-time scaffolding only
  (`dotnet aspnet-codegenerator`); nothing in the app references it. It was the sole source of the four
  `NU1901`/`NU1902`/`NU1903` advisories via `NuGet.Packaging` / `NuGet.Protocol` 6.12.1.
- Pinned `System.Security.Cryptography.Pkcs` to `10.0.13` as a direct reference. `Microsoft.EntityFrameworkCore.SqlServer
  10.0.13 → Microsoft.Data.SqlClient 6.1.7` drags in the vulnerable `9.0.11` (GHSA-846f-f764-76mx high,
  GHSA-2fmv-rv2c-59x7 moderate). Per KB 27 Step 2, a direct top-level reference wins over the transitive one; `10.0.13`
  matches the runtime major of every other framework-tied package.
- Dropped the now-redundant `<Folder Include="wwwroot\photos\" />` (the directory has content).

All framework-tied packages are aligned at `10.0.13`.

## 4. Code changes

### Correctness fix — validation attribute from the wrong namespace
`ViewModels/CreateRoleViewModel.cs` imported `Microsoft.Build.Framework`, so its `[Required]` resolved to
**MSBuild's** `RequiredAttribute` (a task-parameter marker), not `System.ComponentModel.DataAnnotations.RequiredAttribute`.
`RoleName` was therefore **never validated** — `ModelState.IsValid` in `AccountController.CreateRole` always passed for an
empty role name. Switched the import to `System.ComponentModel.DataAnnotations`. The type only resolved at all because
MSBuild assemblies leaked in transitively through the scaffolding package removed above.

**Behavioral change:** creating a role with a blank name is now rejected by model validation, as the attribute clearly
intended.

### `Program.cs`
- `ASP0014`: `app.UseEndpoints(e => e.MapControllerRoute(...))` → top-level `app.MapControllerRoute(...)`. Same route
  template, same pipeline position.
- `CS8600`: `builder.Configuration["ConnectionStrings:DefaultConnection"]` → `GetConnectionString("DefaultConnection")`
  with a `?? throw new InvalidOperationException(...)`. A missing connection string now fails fast at startup with a
  clear message instead of flowing a `null` into `UseSqlServer`.
- `CS8604`: introduced `const string adminRoleName = "admin"` and used it for both `new IdentityRole(...)` and
  `AddToRoleAsync(...)` instead of the nullable `adminRole.Name`. Same value, no suppression needed.

### `Controllers/AccountController.cs` — nullable reference warnings (14 of them)
Resolved by handling the nulls rather than blanket-suppressing:
- `Login` / `Register`: `loginmodel.Email!`, `loginmodel.Password!`, `registermodel.Password!` — these sit inside
  `if (ModelState.IsValid)` and the properties carry `[Required]`, so non-null is guaranteed.
- `EditUser(EditUserViewModel)` / `EditRole(EditRoleViewModel)`: a null `model.Id` now short-circuits to the existing
  "hasn't been found" branch instead of being passed to `FindByIdAsync`.
- `EditRole(string)`, `EditUsersInRole` (both overloads): `role?.Name is null` now takes the existing not-found branch,
  and the non-null name is held in a `string roleName` local used throughout the loop.
- `EditRole(string)`: a user with a null `UserName` is no longer added to the display list.
- `ManageUserRoles(string)`: a role with a null `Name` is skipped rather than passed to `IsInRoleAsync`.
- `ManageUserRoles(List<...>, string)` (`CS8620`): `.Where(x => x.IsSelected && x.RoleName is not null).Select(u => u.RoleName!)`
  so `AddToRolesAsync` receives `IEnumerable<string>`.

**Behavioral change (improvement):** in `POST EditUsersInRole`, a posted `UserId` that does not resolve to a user is now
skipped via `continue`. Previously `null` was passed straight into `IsInRoleAsync`, which threw
`ArgumentNullException` and returned a 500 for a crafted or stale form post.

### `Attributes/ImageExtensionAttribute.cs`
`CS8600`: `string path` → `string? path` for the `value as string` assignment. The existing `if (path is not null)` guard
already handled the null case; only the declaration was wrong.

## 5. Containerization (KB 19)

`Dockerfile` still used `mcr.microsoft.com/dotnet/sdk:6.0` and `aspnet:6.0`, which cannot build or run a `net10.0`
project. Updated both to `10.0`. Two further problems in that file were fixed at the same time:

- `COPY PetShopWeb/*.csproj .` placed the project at the image root, then `COPY . .` added `PetShopWeb.sln` and the real
  `PetShopWeb/PetShopWeb.csproj` alongside it — leaving an argument-less `dotnet publish` with multiple candidate
  project/solution files (`MSB1011`). Restore and publish now name the project explicitly, preserving layer caching.
- The .NET 8+ `aspnet` images default to port **8080**, not 80. Added `ENV ASPNETCORE_HTTP_PORTS=80` so the container
  keeps listening on 80 and the existing `docker-compose.yml` mapping (`3000:80`) still works unchanged.
- Dropped `EXPOSE 433`: it was a typo for 443, no HTTPS listener or certificate is configured, and nothing ever bound
  it. `EXPOSE` is declarative only, so the (equally dead) `3001:433` compose mapping is unaffected.

The Docker image was not built or run — no container runtime in this environment, and running the app is out of scope.

---

## Next steps (non-blocking — for human review)

1. **`Program.cs` drops and recreates the database on every startup.** `ctx.Database.EnsureDeleted()` followed by
   `EnsureCreated()` runs unconditionally, so every process start destroys all user data and reseeds the demo animals.
   This is a development-only pattern and is **destructive in test and production**. Left as-is because it is
   pre-existing behavior that the app's data story depends on (there are no EF migrations in the repo), and changing it
   is a product decision. Recommended: guard the block with `if (app.Environment.IsDevelopment())`, add a real EF Core
   migration, and call `ctx.Database.Migrate()` for non-dev environments.
2. **Hard-coded seeded admin credentials.** `admin@admin.com` / `Admin123` are literals in `Program.cs`, and
   `options.Password.RequireNonAlphanumeric = false` weakens the Identity policy globally. Move the seed credentials to
   configuration/user-secrets and force a password change on first sign-in.
3. **Two latent bugs in the admin seeding logic** (pre-existing, not touched because they change behavior):
   `if (!ctx.Roles.Any())` means the `admin` role is not created when *any* role already exists, yet
   `AddToRoleAsync(adminUser, "admin")` runs regardless; and the guard `ctx.Users.Any(u => u.UserName == "Admin")`
   tests `"Admin"` while the user is created as `"admin@admin.com"`, so the check never matches. Both `IdentityResult`s
   are also discarded, so a failed seed is silent.
4. **Connection string points at a Windows LocalDB instance** (`Server=(localdb)\AspNetCoreCourse`), which cannot work
   on Linux or in the container. The commented-out line above it targets the compose `db` service. Supply the real value
   per environment via `ConnectionStrings__DefaultConnection` rather than committing it to `appsettings.json`.
   `appsettings.json` also contains a `//` comment inside JSON — tolerated by the .NET configuration reader, but not
   portable to stricter JSON parsers.
5. **Middleware order.** `UseAuthentication()` currently precedes `UseRouting()`. This works (authentication needs no
   endpoint metadata), so it was left alone, but the canonical ASP.NET Core order is
   `UseRouting` → `UseAuthentication` → `UseAuthorization`.
6. **`AdministratorController` is `[Authorize]` only, not `[Authorize(Roles = "admin")]`,** so any signed-in user can add,
   edit and delete animals. The navigation bar also shows the Administrator menu to anonymous visitors. Confirm whether
   this is the intended access model.
7. **No test project exists.** The migration is verified by compilation only. Adding even a thin test project over the
   repositories and `AccountController` would make future upgrade cycles verifiable rather than build-checked.
8. **`docker-compose.yml`** references prebuilt Docker Hub images (`yarins2000/petshopapp:1.0`) rather than building from
   the updated `Dockerfile`, so it will keep running the old .NET 6 image until those images are rebuilt and pushed, or
   the service is switched to `build: .`.
