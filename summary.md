# PetShopWeb upgrade to net10.0

The source project was already ASP.NET Core 6 (SDK-style, EF Core, Identity, minimal hosting), not .NET Framework. It had no System.Web, EF6, Web.config or Global.asax, so the System.Web, EF6 and config migration steps didn't apply.

## Changes
- `PetShopWeb.csproj`: targets `net10.0`. EF Core and Identity packages are on 10.0.14, and CodeGeneration.Design is on 10.0.2. Added direct pins to override vulnerable transitive packages from CodeGeneration.Design: `NuGet.Packaging`/`NuGet.Protocol` 6.12.5 (fixes NU1901) and `System.Security.Cryptography.Pkcs` 10.0.14 (fixes NU1902/NU1903). The private-feed package `Vutk.LiveCheck.Private` 1.0.0 is unchanged.
- `Program.cs`: uses `GetConnectionString`. The `UseEndpoints` block is now a top-level `MapControllerRoute` (fixes ASP0014). The route and middleware order are unchanged.
- `AccountController.cs` and `ImageExtensionAttribute.cs`: fixed nullable warnings caused by the Identity 10 nullable annotations. There are two small hardening changes:
  - `EditUsersInRole` (POST) now skips user IDs that don't exist instead of throwing.
  - `ManageUserRoles` (POST) ignores null role names.
- `Dockerfile`: now uses the `sdk:10.0` and `aspnet:10.0` images. Added `ASPNETCORE_HTTP_PORTS=80`, because .NET 8+ images listen on 8080 by default. This keeps the `3000:80` mapping in docker-compose working.

## Result
`dotnet build PetShopWeb/PetShopWeb.csproj` finishes with 0 errors and 0 warnings.

## Next steps
- The Docker build runs `dotnet restore` against the private feed `Vutk.LiveCheck.Private`. The image build needs a NuGet.config and credentials for that feed, which the Dockerfile doesn't provide yet.
- Existing behavior I didn't change, for review:
  - `Program.cs` calls `EnsureDeleted()` on every startup, which wipes the database.
  - The admin seed check compares `UserName == "Admin"`, but the user is created as `admin@admin.com`.
  - The admin password is hard-coded.
- `Dockerfile` exposes port `433`, which is probably a typo for 443. It's kept as-is to match docker-compose.
- The repository has no test project, so I couldn't run any tests.
