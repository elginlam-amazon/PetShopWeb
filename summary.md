# PetShopWeb upgrade to net10.0

The project was already ASP.NET Core 6 (SDK-style, EF Core, Identity, minimal hosting), so there was no System.Web, EF6, or Global.asax code to migrate.

## Changes
- `PetShopWeb.csproj`: targets `net10.0`. EF Core, Identity, and Tools packages are on 10.0.14, and CodeGeneration.Design is on 10.0.2. Vulnerable transitive packages are pinned: NuGet.Packaging/NuGet.Protocol 6.12.5 and System.Security.Cryptography.Pkcs 10.0.14.
- `Program.cs`: replaced `UseEndpoints` with a top-level `MapControllerRoute` (ASP0014). Same route template and same middleware order.
- Nullable warnings: after .NET 6, the Identity APIs got nullable annotations, which produced new warnings. They are resolved with null-forgiving operators in `AccountController`, `Program.cs`, and `ImageExtensionAttribute`, so runtime behavior is unchanged.
- `Dockerfile`: now uses the SDK/ASP.NET 10.0 images. Sets `ASPNETCORE_HTTP_PORTS=80`, because .NET 8+ images listen on 8080 by default and `docker-compose.yml` maps `3000:80`.

## Result
`dotnet build` gives 0 errors and 0 warnings. No vulnerable packages are reported.

## Next steps
- There is no test project, so nothing was verified at runtime.
- `Program.cs` runs `EnsureDeleted()` on every startup, so the database is wiped each time. This was already the behavior before the upgrade and is left as is; it should be reviewed before production use.
- The published `yarins2000/petshopapp:1.0` image referenced in `docker-compose.yml` needs to be rebuilt from the updated Dockerfile.
