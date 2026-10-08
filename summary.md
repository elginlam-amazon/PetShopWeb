# PetShopWeb upgrade: net6.0 → net10.0

## Starting point
PetShopWeb was already an SDK-style ASP.NET Core 6 MVC app (minimal hosting, EF Core, ASP.NET Core Identity). It had no System.Web, EF6, Global.asax or Web.config, so no Framework-to-Core code migration was needed. The TFM and package versions had already been moved to net10.0 / 10.0.x.

## Changes
- `PetShopWeb.csproj`: targets `net10.0`. Packages are Identity.EntityFrameworkCore, EF Core Proxies, SqlServer and Tools 10.0.12, plus CodeGeneration.Design 10.0.2. `NuGet.Packaging` and `NuGet.Protocol` are pinned to 6.12.5 because the scaffolding package pulls in 6.12.1, which is affected by GHSA-g4vj-cjjj-v7hg (NU1901).
- `Program.cs`:
  - The connection string is now read with `GetConnectionString`, and startup fails fast with a clear error if it is missing. Before, a null value was passed to `UseSqlServer`, which also failed.
  - `UseEndpoints` is replaced with top-level `app.MapControllerRoute` (ASP0014). The route template is unchanged.
- `AccountController.cs`: fixed nullable warnings that appeared because Identity 10 has nullability annotations.
  - Values already checked by `[Required]` / ModelState, and role and user names on stored entities, are marked with `!`.
  - A null `Id` on a posted model now goes to the existing NotFound or RolesManager path.
  - Small behaviour change: in `EditUsersInRole` (POST), a user id that doesn't resolve is now skipped. Before, it threw `ArgumentNullException`.
  - In `ManageUserRoles` (POST), selected rows with a null `RoleName` are ignored.
- `ImageExtensionAttribute.cs`: `path` is now declared `string?` (CS8600). Behaviour is unchanged.
- `Dockerfile`: uses the sdk/aspnet 10.0 images and sets `ASPNETCORE_HTTP_PORTS=80`, because .NET 8+ images listen on 8080 by default. This keeps the docker-compose `3000:80` mapping working.

## Verification
`dotnet build PetShopWeb/PetShopWeb.csproj --no-incremental` gives 0 errors and 0 warnings. The repository has no test projects.

## Next steps
- Startup calls `EnsureDeleted()` / `EnsureCreated()` every time, which wipes the database on each start. This is existing behaviour and was kept, but it should be limited to Development before any real deployment.
- The `yarins2000/petshopapp:1.0` image named in docker-compose was built on .NET 6. Rebuild and publish it from the updated Dockerfile.
- Microsoft.Data.SqlClient (pulled in by EF Core 10) encrypts connections by default. The existing connection strings already set `TrustServerCertificate=True`, but check any production connection string.
