# Migration Summary: PetShopWeb .NET Framework → net10.0

## Status: ✅ COMPLETE — Zero Errors, Zero Warnings

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## What Was Done

The AWS Transform Custom CLI (atx) completed the migration from .NET Framework (4.5–4.8) to net10.0 in its preceding pass. The migration covered all critical areas; no additional compilation errors required fixing.

### Project File (`PetShopWeb.csproj`)
- Converted to SDK-style project (`Microsoft.NET.Sdk.Web`)
- Target framework set to `net10.0`
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Implicit usings enabled
- All legacy NuGet packages replaced with ASP.NET Core / EF Core 10 equivalents:
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.14
  - `Microsoft.EntityFrameworkCore.SqlServer` 10.0.14
  - `Microsoft.EntityFrameworkCore.Proxies` 10.0.14 (lazy loading)
  - `Microsoft.EntityFrameworkCore.Tools` 10.0.14 (design-time only)
  - `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2 (design-time only)
  - Security pins: `System.Security.Cryptography.Pkcs` 10.0.14, `NuGet.Packaging` / `NuGet.Protocol` 6.12.5

### Application Lifecycle (`Program.cs`)
- Replaced `Global.asax` / `Startup.cs` with minimal hosting model (`Program.cs`)
- DI container wired for all four repositories (`AnimalRepository`, `CommentRepository`, `CategoryRepository`, `AccountRepository`)
- EF Core configured with `UseLazyLoadingProxies().UseSqlServer(connectionString)`
- ASP.NET Core Identity configured with `AddIdentity<IdentityUser, IdentityRole>().AddEntityFrameworkStores<PetShopContext>()`
- Middleware pipeline in correct order: `UseStaticFiles` → `UseRouting` → `UseAuthentication` → `UseAuthorization`
- Exception handling: `UseExceptionHandler("/Error/ErrorPage")` for non-Development environments
- Default route: `{controller=Home}/{action=HomePage}/{id?}`
- Development seed: creates admin role and admin user from `SeedAdmin:Password` configuration

### Data Access (`PetShopContext`)
- Migrated from EF6 `DbContext` → EF Core `IdentityDbContext` (merges Identity tables into same DB)
- `HasData` seed intact for 5 categories, 10 animals, 4 comments
- Lazy-loading navigation properties (`virtual`) preserved on `Animal`, `Category`, `Comment`

### Authentication & Authorization
- Migrated from OWIN/Katana Identity to ASP.NET Core Identity
- `AccountController` uses `UserManager<IdentityUser>`, `SignInManager<IdentityUser>`, `RoleManager<IdentityRole>`
- `[Authorize(Roles = "admin")]` on administrator-facing controllers

### Removed Legacy Artifacts
- `Web.config` — removed (configuration migrated to `appsettings.json`)
- `packages.config` — removed (SDK-style NuGet references in `.csproj`)
- `Global.asax` — removed (replaced by `Program.cs`)
- `System.Web.*` references — none remain
- `System.Data.Entity` references — none remain

---

## Next Steps (Action Required Before Production)

### [CRITICAL] Replace the LocalDB connection string
`appsettings.json` still has `Server=(localdb)\\AspNetCoreCourse` which only works on Windows. On Linux/container, override via environment variable:
```bash
export ConnectionStrings__DefaultConnection="Server=localhost,1433;Database=PetShop;User ID=sa;Password=<pw>;Encrypt=True;TrustServerCertificate=True;"
```

### [CRITICAL] Create EF Core migrations for production schema management
There is no `Migrations/` folder. `EnsureDeleted()` + `EnsureCreated()` in Development wipes the database on every start. For staging/production:
```bash
cd /data/code/PetShopWeb
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef migrations add InitialCreate
dotnet ef database update
```
Then replace `EnsureDeleted()` / `EnsureCreated()` in `Program.cs` with `ctx.Database.Migrate()`.

### [HIGH] Set a real `SeedAdmin:Password` in user secrets
`appsettings.Development.json` has `"Password": "<<SECRET>>"` — this placeholder has no digit, so the Identity password policy rejects it and the admin account is never created:
```bash
cd /data/code/PetShopWeb
dotnet user-secrets init
dotnet user-secrets set "SeedAdmin:Password" "<a-strong-password>"
```

### [MEDIUM] Guard the connection string against null
In `Program.cs`, prefer the null-safe accessor:
```csharp
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
```

### [MEDIUM] Review `RequireNonAlphanumeric = false` password policy
This weakens the Identity default policy for all users. Remove it unless there is a documented reason.

### [LOW] EF Core 7 → 10 lazy-loading runtime validation required
The compiler cannot catch broken LINQ translations across 3 major EF Core versions. Smoke-test these flows after connecting a real database:
- `/Catalog` (uses `Category.Animals` / `Animal.Category`)
- `/AnimalDetails/{id}` (uses `Animal.Comments`)
- `/Administrator` — create/edit/delete/upload
- `/Account` — register, login, logout

### [LOW] `wwwroot/photos` on Linux needs write permissions
Uploaded animal images are saved to `wwwroot/photos`. Ensure the app user owns this directory; on container deployments back it with a persistent volume.

### [LOW] Remove scaffolding tools from production builds
`Microsoft.VisualStudio.Web.CodeGeneration.Design` and its three transitive vulnerability pins (`System.Security.Cryptography.Pkcs`, `NuGet.Packaging`, `NuGet.Protocol`) are design-time only. If scaffolding is not used in CI, consider removing them.
