# PetShopWeb upgrade to net10.0

The project was already on ASP.NET Core 6 (SDK-style, EF Core, Identity), not .NET Framework. It had no System.Web, EF6 or Web.config/Global.asax code to migrate, so the work was version alignment and cleanup.

## Changes
- `PetShopWeb.csproj`: target is `net10.0`. EF Core, Identity and CodeGeneration packages are on 10.0.x. The transitive `NuGet.Packaging` and `NuGet.Protocol` packages are pinned to 6.12.5 to clear NU1901 (GHSA-g4vj-cjjj-v7hg).
- `Program.cs`: `UseEndpoints` is replaced with top-level `app.MapControllerRoute` (ASP0014). The routes are unchanged. Nullability annotations were added.
- `AccountController.cs`: Identity 10 now has nullable annotations, which produced CS8604/CS8620 warnings. Null-forgiving operators were added where values are already required by validation or come from the DB. In `EditUsersInRole` (POST), an unknown user id is now skipped. Before, it threw `ArgumentNullException`.
- `ImageExtensionAttribute.cs`: `path` is now `string?`.
- `Dockerfile`: the images are now `sdk:10.0` and `aspnet:10.0`. `ASPNETCORE_HTTP_PORTS=80` is set so the app keeps listening on the port that docker-compose maps (.NET 8+ images listen on 8080 by default).

## Result
`dotnet build PetShopWeb/PetShopWeb.csproj`: 0 errors, 0 warnings.

## Next steps
- `ViewModels/CreateRoleViewModel.cs` uses `Microsoft.Build.Framework.Required`, which is not a validation attribute, so `RoleName` is not validated. It probably should be `System.ComponentModel.DataAnnotations.Required`. I left it alone because changing it changes validation behaviour. It also only compiles because CodeGeneration.Design brings in Microsoft.Build.Framework transitively.
- This upgrade did not change an existing Dockerfile problem: after `COPY . .`, `/app` contains both `PetShopWeb.csproj` and `PetShopWeb.sln`, so `dotnet publish` with no arguments may fail with MSB1011. Consider `dotnet publish PetShopWeb/PetShopWeb.csproj`.
- The aspnet:10.0 image runs as non-root user `app`. Binding to port 80 depends on Docker allowing unprivileged low ports (the default on Docker 20.10+). Otherwise, switch to 8080 and update docker-compose.
- `Program.cs` calls `EnsureDeleted()` on every startup, so the database is wiped each time. This behaviour was already there and is unchanged.
- The connection string is in `appsettings.json` (a commented-out entry contains an sa password). Move secrets to user-secrets or environment variables for each environment.
- The docker-compose images (`yarins2000/petshopapp:1.0`) are prebuilt on .NET 6 and need to be rebuilt.
