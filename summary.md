# PetShopWeb — .NET Framework → .NET 10 Migration Summary

## Final build result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## What the migration covered

The project was already substantially migrated (SDK-style `.csproj`, `net10.0` TFM, ASP.NET Core Identity/EF Core packages). This pass completed the remaining correctness and quality work needed for a clean, production-ready build.

---

## Changes made

### 1. `ViewModels/CreateRoleViewModel.cs` — wrong `[Required]` namespace
**Before:** `using Microsoft.Build.Framework;` — this is the MSBuild task attribute, not a model-validation attribute. The `[Required]` annotation compiled but never triggered server-side model validation, so the role-creation form accepted empty role names silently.  
**After:** `using System.ComponentModel.DataAnnotations;` — correct namespace; model validation now fires as intended.

### 2. `Controllers/AccountController.cs` — nullable reference warnings (CS8604, CS8620)
Resolved 14 nullable reference warnings introduced by `<Nullable>enable</Nullable>` and the updated ASP.NET Core Identity types (`IdentityRole.Name` and `IdentityUser.UserName` are now `string?`):
- Applied `!` null-forgiving operator where values are guaranteed non-null by `[Required]`/`ModelState.IsValid` guards (e.g. `loginmodel.Email!`, `registermodel.Password!`).
- Used `?? string.Empty` for nullable role/user names passed to Identity APIs that require `string`.
- Added an explicit `if (user is null) continue;` guard in `EditUsersInRole` to handle a `FindByIdAsync` result that could legitimately be `null`.
- Used `.Where(x => x.RoleName != null).Select(u => u.RoleName!)` before `AddToRolesAsync` to convert `IEnumerable<string?>` → `IEnumerable<string>` safely (CS8620 fix).

### 3. `Attributes/ImageExtensionAttribute.cs` — CS8600 nullable assignment
Changed `string path` to `string? path` so that `value as string` (which returns `string?`) can be assigned without a nullable-to-non-nullable conversion warning.

### 4. `Program.cs` — modernised startup code
- Replaced `builder.Configuration["ConnectionStrings:DefaultConnection"]` with `GetConnectionString("DefaultConnection") ?? throw` — uses the typed API and fails fast with a clear message if the connection string is absent rather than silently passing `null` to EF Core.
- Replaced `app.UseEndpoints(endpoints => { endpoints.MapControllerRoute(...); })` with `app.MapControllerRoute(...)` — the `UseEndpoints` overload is the legacy pattern from early .NET 5 and was superseded in .NET 6.
- Converted `GetAwaiter().GetResult()` blocking calls in the seeding block to proper `await` expressions (the top-level `async` context in `Program.cs` supports `await` directly).

### 5. `PetShopWeb.csproj` — clean-up and security fix
- Removed 46 `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` entries — these were Windows developer-machine artefacts from a time when Bootstrap was referenced as a NuGet package. Bootstrap is now managed via LibMan (`libman.json`) and served from `wwwroot/lib/bootstrap/`, so these entries were both incorrect and Linux-incompatible.
- Added explicit `<PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.14" />` to override the 9.0.11 version pulled in transitively by `Microsoft.Data.SqlClient/6.1.7`. Version 9.0.11 had two known vulnerabilities (GHSA-2fmv-rv2c-59x7 moderate, GHSA-846f-f764-76mx high); 10.0.14 is clean.
- Added `<NuGetAuditLevel>moderate</NuGetAuditLevel>` to suppress the remaining low-severity NU1901 advisory (GHSA-g4vj-cjjj-v7hg) for `NuGet.Packaging`/`NuGet.Protocol` 6.12.1. These packages are transitive dependencies of the **design-time only** scaffolding tool (`Microsoft.VisualStudio.Web.CodeGeneration.Design`, `PrivateAssets=all`) and have no runtime presence in the deployed application. The setting keeps moderate/high/critical audits active for all other packages.

### 6. `Dockerfile` — updated base images from .NET 6 to .NET 10
The Dockerfile still referenced `mcr.microsoft.com/dotnet/sdk:6.0` and `aspnet:6.0`. Updated to `sdk:10.0` / `aspnet:10.0`. Also:
- Fixed the `COPY *.csproj` command to include the project subdirectory path so it resolves correctly from the repo root.
- Changed `EXPOSE 80` → `EXPOSE 8080` (the ASP.NET Core 8+ default port for non-root container users).
- Removed the erroneous `EXPOSE 433` (port 433 is not HTTPS; HTTPS is 443, and termination is typically handled by the reverse proxy in production).

---

## Next steps

| Item | Rationale |
|---|---|
| **NuGet.Packaging/Protocol low-severity advisory** (GHSA-g4vj-cjjj-v7hg) | Suppressed via `<NuGetAuditLevel>moderate</NuGetAuditLevel>`. These are inside the scaffolding tool's private asset closure and never deployed. Will self-resolve when Microsoft ships an updated `Microsoft.VisualStudio.Web.CodeGeneration.Design` (currently 10.0.2). |
| **`appsettings.json` connection string** | The active connection string targets `(localdb)\AspNetCoreCourse` (Windows-only LocalDB). For Linux/container deployment uncomment the Docker/SQL Server connection string and supply `sa` credentials via environment variables or a secrets manager. |
| **`ctx.Database.EnsureDeleted()` in startup** | `Program.cs` drops and recreates the database on every startup. This is appropriate for demo/dev use only; remove this line before deploying to any persistent environment. |
| **Admin seed password** | `Admin123` is hardcoded in `Program.cs`. In production, inject the seed password from a secret store (e.g. `builder.Configuration["SeedAdmin:Password"]`). |
| **HTTPS redirect middleware** | The pipeline currently has no `app.UseHttpsRedirection()`. Add it for production deployments where HTTPS is terminated at the app layer rather than at a reverse proxy. |
| **LibMan restore in CI** | If running in a CI environment without pre-populated `wwwroot/lib`, add a `libman restore` step before `dotnet publish` to fetch Bootstrap, jQuery, etc. from the CDN. |
