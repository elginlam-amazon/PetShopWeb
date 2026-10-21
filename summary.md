# PetShopWeb — .NET 10 (`net10.0`) Migration Summary

## Result

| Check | Outcome |
| --- | --- |
| `dotnet build PetShopWeb/PetShopWeb.csproj` (Debug) | **exit 0 — 0 errors, 0 warnings** |
| `dotnet build PetShopWeb/PetShopWeb.csproj -c Release` | **exit 0 — 0 errors, 0 warnings** |
| `dotnet publish -c Release` | **exit 0** |
| Razor views compiled | 23/23 view classes emitted into `PetShopWeb.dll` |
| `System.Web.*` / `System.Data.Entity` / `System.Web.Http.*` references | none |

Warnings went from **27 → 0**.

## Starting state

CodeKB (index of the pre-upgrade source) showed the module was already an ASP.NET Core
MVC app — `Controller`/`IActionResult` controllers, constructor-injected repositories,
`IWebHostEnvironment`, EF Core `IdentityDbContext`, a top-level `Program.cs`. There was
no Web Forms, no `Global.asax`, no `Web.config`, no WCF, no `System.Drawing`, no OWIN and
no test project, so KB docs 02–04, 10, 14, 20–26 and 28 did not apply. The project file
was already SDK-style on `net10.0` with the `10.0.x` framework-tied packages in place.

The remaining work was therefore: an unbuildable workspace, project-file cleanup, the
restore-integrity advisories, the .NET 6 container images, and the full warning set.

## Changes

### Build blocker (`MSB3374`)
Stale root-owned `obj/`/`bin/` directories from a previous host made the Razor
`MvcApplicationPartsAssemblyInfo.cache` unwritable, failing the build before the compiler
ran. Removed them; they are regenerated on build and are already git-ignored.

### `PetShopWeb/PetShopWeb.csproj` (KB 01, 06, 27)
- Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\..." />`
  items. These were Visual-Studio-generated absolute Windows paths pointing at a developer
  machine's NuGet cache — meaningless on any other machine and on Linux.
- Removed the empty `<Folder Include="wwwroot\photos\" />` item (the folder has content, so
  the marker was redundant, and it used a Windows path separator).
- Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2. This is a design-time
  scaffolding tool (`dotnet-aspnet-codegenerator`) that the application never uses, and it
  was the sole source of the `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 advisories
  (`NU1901`). Scaffolding, if needed again, is available via the `dotnet-aspnet-codegenerator`
  global tool without shipping these assemblies with the app.
- Added a pinned `System.Security.Cryptography.Pkcs` **10.0.13** (KB 27 Step 3). `Microsoft.EntityFrameworkCore.SqlServer`
  10.0.13 → `Microsoft.Data.SqlClient` 6.1.7 was resolving a vulnerable **9.0.11**
  (`NU1902` moderate + `NU1903` high). The pin is at the .NET 10 runtime major, matching the
  rest of the stack per the KB's framework-tied-package rule. The reason is recorded as a
  comment in the project file so the pin is not dropped by accident.

### `Dockerfile` (KB 05, 19)
Still built and ran the app on **.NET 6** images, which cannot host a `net10.0` assembly.
- `sdk:6.0` → `sdk:10.0`, `aspnet:6.0` → `aspnet:10.0`.
- Replaced `EXPOSE 80` / `EXPOSE 433` with `EXPOSE 8080` + `ASPNETCORE_HTTP_PORTS=8080`.
  .NET 8+ container images run as a non-root user and default to port 8080, so port 80 no
  longer binds. `433` was in any case a typo for the HTTPS port `443` and was never served —
  the app configures no HTTPS endpoint in the container, so it has simply been dropped
  rather than carried forward. **This changes the container's published port** — see
  Next steps for the matching `docker-compose.yml` change.

### `ViewModels/CreateRoleViewModel.cs` — latent validation bug
The file imported `Microsoft.Build.Framework`, so its `[Required]` resolved to
**MSBuild's** `RequiredAttribute` (for task parameters), not
`System.ComponentModel.DataAnnotations.RequiredAttribute`. MVC model validation ignores it
entirely, so `CreateRole` accepted an empty role name despite looking validated. The type
was only on the classpath as a transitive asset of the scaffolding package removed above.
Switched the import to `System.ComponentModel.DataAnnotations`.
**Behavioural change (intended):** `RoleName` is now genuinely required, and an empty
submission returns the view with a validation error instead of attempting to create a
nameless role.

### Nullability warnings — `CS8600` / `CS8604` / `CS8620` (16 warnings)
`<Nullable>enable</Nullable>` was on but the code predated it. Each warning was resolved by
handling the null case, not by suppressing it.

- **ViewModels**: properties that are both `[Required]` and never assigned from a nullable
  source are now non-nullable with an empty-string initializer —
  `LoginViewModel.Email`/`.Password`, `RegisterViewModel.Email`/`.Password`,
  `CreateRoleViewModel.RoleName`. These already carried an explicit `[Required]`, so MVC's
  implicit required-for-non-nullable rule adds nothing and **validation behaviour is
  unchanged**. `EditUserViewModel.Email`/`.UserName` were deliberately left nullable because
  they are populated from `IdentityUser`'s nullable `Email`/`UserName`.
- **Collection properties**: `EditRoleViewModel.Users` and `EditUserViewModel.Claims`/`.Roles`
  are now non-nullable with `= new()` initializers, replacing the hand-written constructors.
  They were never actually null, so the `Model.Users!` style already used in the views
  remains valid and the views were left untouched.
- **`AccountController`**: `FindByIdAsync` calls guarded against a null id
  (`EditUser`, `EditRole`, `EditUsersInRole`); `IdentityRole.Name` captured into a
  non-nullable local after a `role?.Name is null` check (`EditRole`, `EditUsersInRole`,
  `ManageUserRoles`); null `UserName` skipped when building `EditRoleViewModel.Users`;
  `AddToRolesAsync` fed `.OfType<string>()` instead of an `IEnumerable<string?>`.
- **`Attributes/ImageExtensionAttribute`**: `string path` → `string? path`, which is what the
  `value as string` assignment on the next line actually produces. The existing
  `if (path is not null)` guard below it was already written for this.
- **`Program.cs`**: `builder.Configuration["ConnectionStrings:DefaultConnection"]` →
  `builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new
  InvalidOperationException(...)`. Previously a missing connection string silently produced a
  null and failed later with an opaque error; it now fails fast at startup with a named cause.
- **`Program.cs` seeding**: the admin role name is now a `const string adminRoleName = "admin"`
  used for both `new IdentityRole(...)` and `AddToRoleAsync`, instead of reading back the
  nullable `adminRole.Name`.

**One intentional behavioural change:** in `EditUsersInRole` (POST), a `userId` that no longer
resolves to a user is now skipped. The previous code passed the null straight into
`IsInRoleAsync`, which threw an `ArgumentNullException` and failed the whole request — so
this turns a 500 on stale form data into the remaining rows being processed correctly.

### `ASP0014` — `Program.cs`
`app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))` → top-level
`app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")` (KB 10).
Same route table; this is the supported form since .NET 6 and the analyzer's recommendation.

## Next steps (non-blocking)

These are pre-existing issues outside the scope of the framework upgrade. None of them
blocks the build.

1. **`docker-compose.yml` port mapping is now stale.** It maps `3000:80` and `3001:433`, but
   the container listens on **8080** after the Dockerfile update. Change the app service to
   `"3000:8080"` and drop the `3001` mapping. Left unchanged here because editing the
   compose file also means re-tagging the hard-coded published images
   (`yarins2000/petshopapp:1.0`, `yarins2000/petshopdb:1.0`), which is a release decision.
   The compose file also still carries the obsolete `version: '3.3'` key.
2. **`Program.cs` calls `ctx.Database.EnsureDeleted()` on every startup.** This drops and
   recreates the database — including all user data — each time the app boots, in *every*
   environment. This is almost certainly dev-only scaffolding that should be guarded by
   `app.Environment.IsDevelopment()` and replaced with EF Core migrations
   (`ctx.Database.Migrate()`) for test and prod. Left as-is because changing it alters data
   lifecycle behaviour and needs a product decision. **This is the highest-risk item here.**
3. **Seeded admin credentials are hard-coded** (`admin@admin.com` / `Admin123`) and
   `options.Password.RequireNonAlphanumeric = false` weakens the password policy globally.
   Move the seed credentials to configuration / user-secrets and reconsider the policy
   relaxation.
4. **Seeding logic bug:** the guard reads `ctx.Users.Any(u => u.UserName == "Admin")` but the
   user created is `admin@admin.com`, so the guard can never match. Also, the result of
   `userManager.CreateAsync` is assigned but never checked, and `AddToRoleAsync` runs
   regardless of whether it succeeded.
5. **The default connection string targets `(localdb)`**, which is Windows-only. For Linux
   or container runs, `appsettings.json` needs the (currently commented-out) `Server=tcp:db,1433`
   form, or a per-environment override. Note the live string also sets
   `TrustServerCertificate=True`, which disables certificate validation — acceptable for
   local dev, not for prod.
6. **No test project exists**, so there is no automated check that the behavioural fixes above
   preserve intent. The null-guard and validation changes in `AccountController` and
   `CreateRoleViewModel` are the ones worth covering first.
7. **`libman.json` declares `bootstrap-toggle@2.2.2`** but `wwwroot/lib/bootstrap-toggle/`
   is not present in the tree, and `package.json` lists npm packages that overlap with the
   libman-restored ones. Worth reconciling to a single client-asset pipeline (KB 09).
