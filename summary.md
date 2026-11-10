# Migration Summary: .NET 6.0 → .NET 10.0

## Project
`PetShopWeb/PetShopWeb.csproj` — ASP.NET Core MVC application (PetShop)

## Final Build Status
**Build succeeded. 0 Errors. 0 Warnings.**

---

## Iteration 2 changes (net6→net10 compilation cleanup)

### `PetShopWeb.csproj`
- `TargetFramework` already at `net10.0`; all package references already at `10.0.x`
- Added explicit `PackageReference` for `System.Security.Cryptography.Pkcs` 10.0.14 to override vulnerable transitive 9.0.11 (GHSA-2fmv-rv2c-59x7 moderate, GHSA-846f-f764-76mx high)
- Added explicit `PackageReference` for `NuGet.Packaging` 6.13.2 and `NuGet.Protocol` 6.13.2 to override vulnerable transitive 6.12.1 (GHSA-g4vj-cjjj-v7hg low)

### `Program.cs`
- Fixed CS8600: used `GetConnectionString()` with a null-throwing fallback
- Fixed CS8604: added `!` null-forgiving on `adminRole.Name!`
- Fixed ASP0014: replaced `app.UseEndpoints(...)` with top-level `app.MapControllerRoute(...)`

### `Attributes/ImageExtensionAttribute.cs`
- Fixed CS8600: changed `string path` local to `string? path`

### `Controllers/AccountController.cs`
- Fixed CS8604/CS8620 nullable reference warnings throughout (null-forgiving operators, null guards, `Where(x => x.RoleName != null)` filter)

---

## Iteration 3 changes (recommendations from previous iteration)

### `PetShopWeb/Migrations/` (new)
- Generated `InitialCreate` migration via `dotnet ef migrations add InitialCreate`
- Migration captures the full schema: Identity tables, Categories, Animals, Comments, plus all `OnModelCreating` `HasData` seed rows

### `Program.cs` (rewritten)
- **EnsureDeleted → Development-only**: `await ctx.Database.EnsureDeletedAsync()` now runs only when `IsDevelopment()`, preventing destructive wipes in production
- **EnsureCreated → Database.Migrate()**: replaced with `await ctx.Database.MigrateAsync()` so schema is applied idempotently via migrations
- **Async seeding**: all `GetAwaiter().GetResult()` calls replaced with `await`; scope opened with `await using var scope = app.Services.CreateAsyncScope()`
- **Admin password externalised**: hardcoded `"Admin123"` replaced with `builder.Configuration["Seed:AdminPassword"]` — throws `InvalidOperationException` at startup if the value is missing, making misconfiguration loud
- **Bug fix**: corrected user-existence check from `UserName == "Admin"` to `UserName == "admin@admin.com"` to match the created username
- **Simplified role seeding**: `adminRole.Name!` literal replaced with string `"admin"` to avoid null-forgiving after the role is saved

### `appsettings.Development.json`
- Added `"Seed": { "AdminPassword": "Admin123" }` — provides the demo password for local development only

### `appsettings.json`
- Removed invalid JSON comment (the commented-out Docker connection string); Docker deployments override via `ConnectionStrings__DefaultConnection` environment variable

### `Dockerfile`
- Base images updated from `mcr.microsoft.com/dotnet/sdk:6.0` / `aspnet:6.0` to `sdk:10.0` / `aspnet:10.0`
- Fixed `COPY` path so the `.csproj` restore step works correctly from the repo root
- Corrected `EXPOSE 433` typo → `EXPOSE 443`

### `docker-compose.yml`
- Changed `app` service from pre-built image reference to `build: .` so the updated Dockerfile is used
- Added `environment` block with `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__DefaultConnection`, and `Seed__AdminPassword` — all required values are now explicit

---

## Next steps

- 🧪 Run smoke / integration tests against a real SQL Server: lazy loading, sign-in/sign-out, role management, animal CRUD, and comments.
- 🌱 Verify `OnModelCreating` seed data and the admin login (`admin@admin.com` / password from `Seed:AdminPassword`) on a fresh database after the migration.
- 🐳 Build the Docker image with `docker-compose build`, set a secure `Seed__AdminPassword` override, run `docker-compose up`, and test on `localhost:3000`.
- 🔑 For non-demo deployments, replace the `Seed__AdminPassword` value in `docker-compose.yml` with a secrets-manager reference (Docker secrets, Vault, Azure Key Vault, etc.) rather than a plaintext env var.
- 🧪 Add a test project (e.g. xUnit + EF Core InMemory provider) covering the repositories and controllers.
- 🔄 Monitor `dotnet list package --vulnerable --include-transitive` and remove the `NuGet.Packaging` / `NuGet.Protocol` / `System.Security.Cryptography.Pkcs` transitive pins once the parent packages ship fixed versions.
