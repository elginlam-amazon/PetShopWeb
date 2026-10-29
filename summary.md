# PetShopWeb migration to net10.0

## Starting point
PetShopWeb was already an SDK-style ASP.NET Core MVC app on `net6.0` (EF Core, ASP.NET Core Identity, minimal-hosting `Program.cs`). It had no `System.Web`, `System.Data.Entity`, `Web.config` or `Global.asax`, so none of the System.Web/EF6 migration steps applied. The project file had already been retargeted to `net10.0` and its packages bumped to 10.0.x.

## Changes
- **PetShopWeb.csproj**: added direct pins for vulnerable transitive packages. Before this change the build reported NU1901, NU1902 and NU1903.
  - `System.Security.Cryptography.Pkcs` 10.0.14 (pulled in by Microsoft.Data.SqlClient and the scaffolding packages).
  - `NuGet.Packaging` / `NuGet.Protocol` 6.12.5, the patched release on the same 6.12 line (pulled in by Microsoft.VisualStudio.Web.CodeGeneration.Design).
- **Program.cs**
  - The connection string is now read with `GetConnectionString("DefaultConnection")`. If it is missing, the app fails at startup with a clear `InvalidOperationException` instead of passing null to `UseSqlServer`.
  - Replaced `UseEndpoints` with top-level `app.MapControllerRoute` (fixes ASP0014). The route is unchanged.
- **AccountController.cs**: fixed nullable warnings that appeared because Identity 7+ added nullable annotations. Values that are guaranteed by `[Required]` model validation or by Identity are now marked non-null. One behavior change: in `EditUsersInRole` (POST), a user id that can't be found is now skipped. Before, it threw a `NullReferenceException`.
- **ImageExtensionAttribute.cs**: `path` is now declared `string?`. Behavior is unchanged.
- **Dockerfile**: changed the SDK and runtime images from 6.0 to 10.0. Set `ASPNETCORE_HTTP_PORTS=80`, because .NET 8+ images listen on 8080 by default and `docker-compose.yml` maps `3000:80`.

## Result
`dotnet build PetShopWeb/PetShopWeb.csproj` and `dotnet build PetShopWeb.sln` finish with 0 errors and 0 warnings. `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.

## Next steps
- The repository has no test project, so behavior was verified by build only.
- `Program.cs` calls `EnsureDeleted()` and then `EnsureCreated()` on every startup in all environments, which wipes the database each time the app starts. This behavior already existed and was kept. Decide whether it should run only in Development.
- The admin seed checks `u.UserName == "Admin"` but creates the user `admin@admin.com`, so the check never matches and the seed relies on the database having just been recreated. This behavior already existed and was kept.
- The seeded admin password (`Admin123`) and a commented-out SQL `sa` password in `appsettings.json` are stored in source. Move them to user secrets or environment variables.
- Dockerfile: `COPY . .` puts `PetShopWeb.sln`, the copied `PetShopWeb.csproj` and the `PetShopWeb/` folder together in `/app`, so `dotnet publish` with no project argument may fail with MSB1011. This layout already existed. Consider `dotnet publish PetShopWeb/PetShopWeb.csproj`. The Docker build was not run in this environment.
- `docker-compose.yml` uses the prebuilt images `yarins2000/petshopapp:1.0` and `yarins2000/petshopdb:1.0`. Rebuild and publish the app image from the updated Dockerfile.
- The project file has `Content Remove` entries with Windows-specific paths (`C:\Users\PC\.nuget\...`). They do nothing on other machines and can be deleted.
- You can drop the NuGet.* pins once a newer `Microsoft.VisualStudio.Web.CodeGeneration.Design` ships patched dependencies.
