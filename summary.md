# PetShopWeb upgrade to net10.0

## Starting point
PetShopWeb was already an ASP.NET Core app (SDK-style, `net6.0`, EF Core, ASP.NET Core Identity), not .NET Framework. The code had no `System.Web` or `System.Data.Entity` usage, so there was no System.Web or EF6 migration to do.

## Changes
- **PetShopWeb.csproj**: target `net10.0`. EF Core / Identity packages moved to 10.0.14, scaffolding package to 10.0.2. Added direct pins for vulnerable transitive packages (NU1901/NU1902/NU1903): `NuGet.Packaging` and `NuGet.Protocol` 6.12.5, `System.Security.Cryptography.Pkcs` 10.0.14.
- **Program.cs**: `GetConnectionString("DefaultConnection")` (nullable-safe), and `UseEndpoints` replaced with top-level `app.MapControllerRoute` (ASP0014). Routes are the same.
- **AccountController.cs**: fixed nullable warnings. Values already checked by `[Required]`/`ModelState.IsValid` use `!`. A null `Id`/`UserId` now goes to the existing "not found" path instead of reaching Identity APIs that throw `ArgumentNullException`. `EditUsersInRole` (POST) skips users that cannot be found instead of throwing. `ManageUserRoles` (POST) leaves out null role names.
- **ImageExtensionAttribute.cs**: `path` declared as `string?`.
- **Dockerfile**: base images changed to `sdk:10.0` / `aspnet:10.0`. Added `ASPNETCORE_HTTP_PORTS=80` because .NET 8+ images listen on 8080 by default, and docker-compose maps `3000:80`.

## Result
`dotnet build PetShopWeb/PetShopWeb.csproj` and `dotnet build PetShopWeb.sln`: 0 errors, 0 warnings.

## Next steps
- `ViewModels/CreateRoleViewModel.cs` uses `Microsoft.Build.Framework.Required`, which is an MSBuild attribute, not a DataAnnotations one. So `RoleName` is not validated as required. It only compiles because the scaffolding package brings in Microsoft.Build.Framework. Switching to `System.ComponentModel.DataAnnotations` would turn on validation, which is a behavior change, so it was left as is for review.
- `Program.cs` calls `EnsureDeleted()` on every startup, which drops the database in every environment. This is existing behavior and was not changed. Consider limiting it to Development.
- The seeded admin password and the commented-out SQL `sa` password are in source and appsettings.json. Move them to user-secrets or environment variables.
- The scaffolding package (`Microsoft.VisualStudio.Web.CodeGeneration.Design`) is only needed at design time. Removing it would drop the NuGet.* pins, but `CreateRoleViewModel` must be fixed first (see above).
- The `docker-compose.yml` images (`yarins2000/petshopapp:1.0`) are prebuilt and must be rebuilt from the updated Dockerfile.
