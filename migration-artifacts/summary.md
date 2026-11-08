# PetShopWeb – .NET Framework → net10.0 Migration Summary

## 1. Migration Status

**COMPLETE** — `dotnet build PetShopWeb/PetShopWeb.csproj` exits with **0 errors, 0 warnings**.

## 2. What Was Already Done (AWS Transform Custom CLI)

The ATX tool successfully performed the bulk of the migration before this pass:

- Converted the project to SDK-style (`Microsoft.NET.Sdk.Web`), targeting `net10.0`
- Removed all `System.Web.*` references; migrated the application to ASP.NET Core MVC
- Replaced EF6 with EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer` + `Microsoft.EntityFrameworkCore.Proxies` 10.0.14)
- Replaced ASP.NET Identity with `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.14
- Migrated `Global.asax` + `Startup` to `Program.cs` (minimal hosting model)
- Migrated configuration to `appsettings.json` / `appsettings.Development.json`
- Updated all controllers to ASP.NET Core equivalents (`Controller`, `IActionResult`, `ViewModels`)
- Registered DI services for repositories and Identity
- Configured cookie authentication paths and antiforgery global filter
- Added `SeedDatabaseAsync` for admin role/user creation

## 3. Changes Made in This Pass

### 3a. NuGet vulnerability remediation (`PetShopWeb/PetShopWeb.csproj`)

| Change | Reason |
|--------|--------|
| Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.2 | Design-time scaffolding tool not needed in migrated project; was transitively pulling in vulnerable `NuGet.Packaging` 6.12.1 (GHSA-g4vj-cjjj-v7hg) and `System.Security.Cryptography.Pkcs` 9.0.11 |
| Added direct pin `System.Security.Cryptography.Pkcs` **10.0.13** | Overrides 9.0.11 transitively pulled by `Microsoft.Data.SqlClient` 6.1.7 → `Azure.Identity`. Fixes CVE-2026-84008 (GHSA-846f-f764-76mx, High) and CVE-2026-70162 (GHSA-2fmv-rv2c-59x7, Moderate). Advisory specifies 10.0.13 as the patched version for the 10.0.x line. |

### 3b. ViewModel nullable-reference-type cleanup

All `[Required]` string properties in form ViewModels were changed from `string?` to `string = string.Empty`. This is the idiomatic C# NRT pattern: a non-nullable property with `[Required]` means the model binder validates the field as required and the property is guaranteed non-null when `ModelState.IsValid`. Eliminated multiple CS8604 and CS8601 warnings throughout `AccountController`.

| File | Changed properties |
|------|--------------------|
| `ViewModels/LoginViewModel.cs` | `Email`, `Password` → `string = string.Empty` |
| `ViewModels/RegisterViewModel.cs` | `Email`, `Password`, `ConfirmPassword` → `string = string.Empty` |
| `ViewModels/EditUserViewModel.cs` | `UserName`, `Email` → `string = string.Empty`; `Claims`, `Roles` → `List<string> = new()` (non-nullable) |
| `ViewModels/EditRoleViewModel.cs` | `RoleName` → `string = string.Empty`; `Users` → `List<string> = new()` (removed old constructor) |
| `ViewModels/UserRolesViewModel.cs` | `RoleName` → `string = string.Empty` |

### 3c. `AccountController.cs` nullable fixes

| Location | Fix | Reason |
|----------|-----|--------|
| `EditUser GET` object initializer | `user.Email!`, `user.UserName!` | `IdentityUser.Email`/`UserName` are `string?` in the library; they are always set for persisted users |
| `EditUser POST` | `FindByIdAsync(model.Id!)` | `EditUserViewModel.Id` is `string?` (hidden form field); `FindByIdAsync` null result is handled immediately after |
| `EditRole GET` | `role.Name!`, `user.UserName!` | `IdentityRole.Name` and `IdentityUser.UserName` are always set for persisted entities |
| `EditRole POST` | Added `if (editModel.Id is null) return View(nameof(RolesManager));` | Guard before `FindByIdAsync(editModel.Id)` to avoid null-forgiving on a truly possible null |
| `EditUsersInRole GET` | `role.Name!` | Same as above |
| `EditUsersInRole POST` | `FindByIdAsync(model[i].UserId!)`, `if (user is null) continue;`, `role.Name!` | Handle the (unlikely) case where a user ID from the loop no longer resolves; prevents null-reference on `IsInRoleAsync` |
| `ManageUserRoles GET` | `role.Name!` | Same as EditRole GET |

## 4. Final Build Result

```
dotnet build PetShopWeb/PetShopWeb.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## 5. Next Steps (deferred — require infrastructure or human decisions)

| Item | Priority | Detail |
|------|----------|--------|
| **Connection string is Windows-only** | CRITICAL | `appsettings.json` still has `Server=(localdb)\AspNetCoreCourse` (LocalDB / Windows integrated auth). Supply `ConnectionStrings__DefaultConnection` as an environment variable for every non-Windows environment (see §8 of the ATX summary). |
| **Admin password not in production config** | CRITICAL | `Seed:AdminPassword` is only in `appsettings.Development.json`. Set `Seed__AdminPassword` in the deployment environment or a secret store. |
| **No EF Core migrations** | HIGH | `SeedDatabaseAsync` uses `EnsureCreatedAsync`. In Development, `EnsureDeletedAsync` runs first (destructive). Recommendation: `dotnet ef migrations add InitialCreate` and switch to `MigrateAsync()`. |
| **No global default-deny authorization policy** | HIGH | Each admin action has `[Authorize(Roles = "admin")]` individually, but there is no fallback policy. A newly added action without an `[Authorize]` attribute would be publicly accessible. |
| **Microsoft.Data.SqlClient version** | MEDIUM | 6.1.7 is the direct dependency from EF Core. Once SqlClient ships 6.1.8+ (which will pull `System.Security.Cryptography.Pkcs` ≥ 9.0.21), the direct pin of `10.0.13` can be removed. Monitor SqlClient issue #4843. |
| **Transitive packages still on .NET 9 era** | LOW | `System.Configuration.ConfigurationManager` 9.0.11 and `System.Security.Cryptography.ProtectedData` 9.0.11 remain in the transitive graph from `Microsoft.Data.SqlClient`. No current vulnerability; revisit when SqlClient updates its dep chain. |
| **Compose image out of date** | LOW | `docker-compose.yml` references `yarins2000/petshopapp:1.0` (built from the original .NET 6 code). Rebuild and push with the .NET 10 `Dockerfile`. |
| **Uploaded photos directory** | LOW | `wwwroot/photos` is mapped as content in the csproj but may not exist or be writable in a container image. Mount a volume or migrate uploads to object storage. |
