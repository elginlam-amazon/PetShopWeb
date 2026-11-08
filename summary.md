# Migration Summary: PetShopWeb — .NET Framework → .NET 10

## Result

**Build: SUCCEEDED — 0 errors, 0 warnings**

The project `PetShopWeb/PetShopWeb.csproj` already targeted `net10.0` with SDK-style format. Migration work focused on resolving a file-permissions build blocker, nullable reference warnings, and NuGet vulnerability advisories.

## Changes Made

### 1. Build blocker — MSB3374 (obj cache permissions)
The stale `obj/Debug/net10.0/PetShopWeb.MvcApplicationPartsAssemblyInfo.cache` file had root-owned timestamps that the SDK could not update. Removed the file; subsequent builds recreate it cleanly.

### 2. Program.cs — nullable & routing
- Changed `string connectionString` → `string? connectionString` (CS8600)
- Added `!` null-forgiving operator on `adminRole.Name!` (CS8604)
- Removed unused `var result =` assignment on `CreateAsync` call
- Replaced deprecated `app.UseEndpoints(...)` with `app.MapControllerRoute(...)` (ASP0014)

### 3. Attributes/ImageExtensionAttribute.cs — nullable
- Changed `string path` → `string? path` in `IsValid` method (CS8600)

### 4. Controllers/AccountController.cs — nullable
- Applied `!` null-forgiving operators to all `[Required]`-validated ViewModel properties passed to Identity APIs (CS8604): `loginmodel.Email!`, `loginmodel.Password!`, `registermodel.Password!`
- Applied `!` to `model.Id!`, `editModel.Id!`, `userId!`, `roleId!` on `FindByIdAsync` / `FindByIdAsync` calls
- Applied `!` to `role.Name!` and `user.UserName!` throughout role-management methods
- Fixed `IEnumerable<string?>` → `IEnumerable<string>` mismatch in `AddToRolesAsync` call (CS8620) using `.Select(u => u.RoleName!)`
- Changed `string id` / `string userId` / `string roleId` parameters to `string?` where nullable callers exist

### 5. PetShopWeb.csproj — vulnerability pins
Pinned three vulnerable transitive packages to safe versions to eliminate NU1901/NU1902/NU1903 advisories:
- `System.Security.Cryptography.Pkcs` 9.0.11 → `10.0.14` (moderate + high severity fixed)
- `NuGet.Packaging` 6.12.1 → `6.13.2` (low severity fixed)
- `NuGet.Protocol` 6.12.1 → `6.13.2` (low severity fixed)

## Next steps

None — the build is clean with zero errors and zero warnings.
