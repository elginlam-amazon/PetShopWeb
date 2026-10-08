# Migration Summary: PetShopWeb → .NET 10

## Status
Build: **SUCCEEDED** — 0 errors, 4 warnings (all NU1901 toolchain advisories)

## What Was Done

### Pre-existing State
The project was already partially migrated: SDK-style `.csproj` targeting `net10.0`, ASP.NET Core Identity, EF Core 10, and a working `Program.cs`.

### Fixes Applied

#### 1. Build Cache Permission Error (MSB3374)
- Deleted stale `obj/Debug/net10.0/` directory that had a read-only cache file blocking the build.

#### 2. Nullable Reference Type Warnings (CS8600, CS8604, CS8620)
- **`ImageExtensionAttribute.cs`**: Changed local variable `string path` to `string? path` to correctly handle null.
- **`Program.cs`**: Used null-coalescing throw for connection string retrieval; added `!` for `adminRole.Name` (set by caller).
- **`LoginViewModel.cs`**: Changed `[Required] string?` → `string` with `= string.Empty` default.
- **`RegisterViewModel.cs`**: Changed `[Required] string?` → `string` with `= string.Empty` default.
- **`EditUserViewModel.cs`**: All required fields made non-nullable; list properties initialized inline.
- **`EditRoleViewModel.cs`**: All properties made non-nullable; removed unused `System.Security` import.
- **`UserRolesViewModel.cs`**: `RoleId` and `RoleName` made non-nullable.
- **`UserRoleViewModel.cs`**: `UserId` made non-nullable.
- **`CreateRoleViewModel.cs`**: Fixed wrong namespace (`Microsoft.Build.Framework` → `System.ComponentModel.DataAnnotations`); made `RoleName` non-nullable.
- **`AccountController.cs`**: Used `!` null-forgiving operator on `IdentityRole.Name` and `IdentityUser.UserName` in contexts where the object is known non-null; added null guard for `FindByIdAsync` result in `EditUsersInRole` loop; populated `EditUserViewModel` with `?? string.Empty` fallbacks.

#### 3. ASP0014 Warning (UseEndpoints)
- **`Program.cs`**: Replaced `app.UseEndpoints(...)` with top-level `app.MapControllerRoute(...)`.

## Remaining Warnings (4)
All are `NU1901` — low-severity vulnerability in the .NET SDK's own `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 transitive dependencies. These are not in the application's package graph and cannot be resolved by modifying the project file.

## Next Steps
- The `NU1901` warnings will be resolved automatically when the .NET SDK is updated to a version that ships a newer NuGet client.
- Consider running `dotnet ef migrations add InitialCreate` and verifying the SQL Server connection string in `appsettings.json` before deploying.
