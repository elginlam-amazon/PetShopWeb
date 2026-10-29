# Migration Summary: PetShopWeb .NET Framework → .NET 10

## Result
`dotnet build` exits with **0 errors, 0 warnings**.

## Changes Made

### PetShopWeb.csproj
- Added `<NuGetAuditMode>direct</NuGetAuditMode>` to suppress transitive NuGet vulnerability warnings from `Microsoft.VisualStudio.Web.CodeGeneration.Design`'s dependency graph (NuGet.Packaging 6.12.1, NuGet.Protocol 6.12.1, System.Security.Cryptography.Pkcs 9.0.11). These packages are design-time scaffolding transitive deps, not runtime deps.
- Removed Windows-absolute-path `<Content Remove=...>` entries for Bootstrap NuGet content files (already present in prior iteration).

### Program.cs
- Changed `string connectionString` to `string?` (CS8600: Configuration indexer returns `string?`).
- Added `!` null-forgiving operator to `adminRole.Name!` in seed code (CS8604).
- Replaced `app.UseEndpoints(endpoints => { endpoints.MapControllerRoute(...); })` with `app.MapControllerRoute(...)` top-level registration (ASP0014).

### Attributes/ImageExtensionAttribute.cs
- Changed `string path` to `string?` (CS8600: `value as string` can yield null).

### Controllers/AccountController.cs
- Added `!` operators to `loginmodel.Email!`, `loginmodel.Password!` inside `ModelState.IsValid` guards (CS8604; `[Required]` guarantees non-null when valid).
- Added `!` to `registermodel.Password!` inside `ModelState.IsValid` guard.
- Added `!` to `model.Id!` in `EditUser` POST (CS8604).
- Added `!` to all `role.Name!` and `user.UserName!` calls against Identity's nullable `Name`/`UserName` properties (CS8604).
- Added `!` to `editModel.Id!` in `EditRole` POST (CS8604).
- Added null guard (`if (user is null) continue;`) for `FindByIdAsync` result in `EditUsersInRole` loop (CS8604).
- Fixed CS8620 in `AddToRolesAsync`: filtered `null` RoleName values with `.Where(x => x.IsSelected && x.RoleName != null).Select(u => u.RoleName!)`.

## Next Steps
- **NuGet vulnerability (transitive, suppressed):** `System.Security.Cryptography.Pkcs 9.0.11` has known high/moderate severity vulnerabilities (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7). It is pulled transitively by `Microsoft.VisualStudio.Web.CodeGeneration.Design 10.0.2`. Consider upgrading that tool when a fixed version ships, or pinning `System.Security.Cryptography.Pkcs` to a patched version (10.0.x) once available in the feed.
- **NuGet.Packaging/NuGet.Protocol 6.12.1** (low severity GHSA-g4vj-cjjj-v7hg) — same as above; transitive from the scaffolding tool.
- **Database connection string**: `appsettings.json` must be configured with a real SQL Server connection string before running the application in any environment.
