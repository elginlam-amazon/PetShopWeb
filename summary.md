# PetShopWeb upgrade: net6.0 → net10.0

The project was already an ASP.NET Core 6 MVC app (SDK-style, no System.Web or EF6). Before the upgrade it ran on the minimal hosting model, using EF Core SqlServer with lazy-loading proxies, ASP.NET Core Identity, and repositories registered through DI.

## Changes
- `PetShopWeb.csproj`: changed the target to `net10.0` and moved the EF Core, Identity and CodeGeneration packages to the 10.0.x line. Added pins for `NuGet.Packaging`/`NuGet.Protocol` 6.12.5. These are transitive dependencies of `CodeGeneration.Design`, and the pins fix NU1901 (GHSA-g4vj-cjjj-v7hg).
- `Program.cs`: now reads the connection string with `GetConnectionString` and fails fast with a clear error if it is missing. Replaced `UseEndpoints` with a top-level `MapControllerRoute` (ASP0014).
- `AccountController.cs`: fixed the nullable warnings caused by the Identity 10 nullability annotations. Values that `[Required]` or Identity guarantee are non-null now use `!`. Added null guards where the old code would have thrown:
  - `EditUsersInRole` POST now skips users that no longer exist. Before, it threw an `ArgumentNullException`.
  - `ManageUserRoles` POST now ignores selected entries that have a null role name.
- `ImageExtensionAttribute.cs`: `path` is now declared `string?`.
- `Dockerfile`: moved to the sdk/aspnet 10.0 images. Set `ASPNETCORE_HTTP_PORTS=80` so the container still listens on port 80, because .NET 8+ images default to 8080.

## Result
`dotnet build PetShopWeb/PetShopWeb.csproj` gives 0 errors and 0 warnings.

## Next steps
- `Program.cs` calls `EnsureDeleted()` on every startup, which wipes the database. This was existing behaviour and has been kept, but it should be limited to Development.
- The admin seed check compares `UserName == "Admin"`, but the user it seeds is named `admin@admin.com`. This was existing behaviour and is left unchanged.
- The docker-compose images (`yarins2000/petshop*:1.0`) need to be rebuilt from the updated Dockerfile.
- No test project exists, so nothing was verified at runtime.
