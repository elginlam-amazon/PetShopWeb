# PetShopWeb upgrade: net6.0 → net10.0

The source was already ASP.NET Core 6 MVC with EF Core and Identity, so there was no System.Web, EF6 or Global.asax code to migrate.

Result: `dotnet build PetShopWeb/PetShopWeb.csproj --no-incremental` gives 0 errors and 0 warnings.

## Changes
- PetShopWeb.csproj: targets `net10.0`. The EF Core and Identity packages are on 10.0.13, and CodeGeneration.Design is on 10.0.2.
- Vulnerable transitive packages from CodeGeneration.Design are pinned to patched versions: NuGet.Packaging/NuGet.Protocol 6.12.5 and System.Security.Cryptography.Pkcs 10.0.13. This fixes NU1901, NU1902 and NU1903.
- Program.cs: `UseEndpoints` is replaced with top-level `app.MapControllerRoute` (ASP0014). The route is unchanged.
- Nullable warnings (CS8600, CS8604, CS8620) came from the stricter .NET 10 Identity annotations. They are fixed in Program.cs, AccountController.cs and ImageExtensionAttribute.cs, mostly with `!` on values that are already validated, such as `[Required]` fields or role/user names loaded from the store.
- Behaviour change: in `AccountController.EditUsersInRole` (POST), a user ID that is not found is now skipped. Before, it threw a NullReferenceException or ArgumentNullException.
- Dockerfile: the base images are now `sdk:10.0` and `aspnet:10.0`. `ASPNETCORE_HTTP_PORTS=80` is set because .NET 8+ images listen on 8080 by default and docker-compose maps to container port 80.

## Next steps
- There is no test project, so only the build was checked. Do a runtime smoke test of login, register and the role screens against SQL Server.
- Startup still calls `EnsureDeleted()` and then `EnsureCreated()`, which wipes the database on every start. This was already in the original code and was left unchanged. It needs a product decision before production; consider EF Core migrations and running it only in Development.
- The Dockerfile copies the whole repo into `/app`, where `PetShopWeb.csproj` and `PetShopWeb.sln` both exist, and then runs `dotnet publish` with no project argument. This may fail with MSB1011 (more than one project or solution file). This was already in the original Dockerfile and was not verified. Consider `dotnet publish PetShopWeb/PetShopWeb.csproj`.
- The csproj still has `<Content Remove="C:\Users\PC\.nuget\...">` entries left over from a developer machine. They are harmless on Linux and could be removed.
- The published Docker Hub images (`yarins2000/petshopapp:1.0`) need rebuilding to pick up net10.0.
