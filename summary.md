# PetShopWeb Migration Summary

## Migration: .NET Framework → net10.0

### Final Build Status
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Changes Made

### 1. `PetShopWeb/PetShopWeb.csproj`
- Removed ~50 Windows-specific `Content Remove` entries referencing `C:\Users\PC\.nuget\packages\bootstrap\...`. These paths do nothing on Linux and were left over from the original Windows development machine.
- Added `<PrivateAssets>all</PrivateAssets>` to `Microsoft.VisualStudio.Web.CodeGeneration.Design` (treats it as a dev-only scaffolding tool, matching the same pattern already applied to `Microsoft.EntityFrameworkCore.Tools`).
- Added direct package reference overrides to resolve transitive NuGet security vulnerabilities:
  - `System.Security.Cryptography.Pkcs` 9.0.11 → **10.0.14** — fixes GHSA-2fmv-rv2c-59x7 (Moderate) and GHSA-846f-f764-76mx (High)
  - `NuGet.Packaging` 6.12.1 → **6.12.5** — fixes GHSA-g4vj-cjjj-v7hg (Low)
  - `NuGet.Protocol` 6.12.1 → **6.12.5** — fixes GHSA-g4vj-cjjj-v7hg (Low)

### 2. `PetShopWeb/ViewModels/CreateRoleViewModel.cs`
- **Bug fix:** `using Microsoft.Build.Framework;` → `using System.ComponentModel.DataAnnotations;`
- This was a latent correctness bug: the `[Required]` attribute from `Microsoft.Build.Framework` is an MSBuild task attribute and has no effect in MVC model validation. The `[Required]` on `RoleName` was silently ignored at runtime. It now correctly uses the data annotation that MVC validates.
- The bug was masked because `Microsoft.Build.Framework` was transitively accessible via the old (un-restricted) `Microsoft.VisualStudio.Web.CodeGeneration.Design` dependency chain.

### 3. `PetShopWeb/Program.cs`
- Connection string: replaced silent `null` assignment with an `InvalidOperationException` guard so a missing `DefaultConnection` fails fast at startup with a clear message.
- `adminRole.Name` → `adminRole.Name!`: the null-forgiving operator is appropriate because `IdentityRole("admin")` always sets `Name`.
- Replaced `app.UseEndpoints(endpoints => { endpoints.MapControllerRoute(...) })` with the top-level `app.MapControllerRoute(...)` to resolve ASP0014 analyzer warning.

### 4. `PetShopWeb/Controllers/AccountController.cs`
Resolved 18 CS8604/CS8620 nullable warnings. Strategy used:
- `loginmodel.Email!` / `loginmodel.Password!` / `registermodel.Password!`: safe because `[Required]` is enforced and `ModelState.IsValid` is checked before these are used.
- `role.Name!` / `user.UserName!` throughout role/user management: safe because the objects are fetched from EF/Identity which always populates these fields.
- `editModel.Id`: added an explicit `if (editModel.Id is null) return NotFound()` guard (better than `!` since Id comes from a form post).
- `EditUsersInRole` loop: added `if (user is null) continue` guard after `FindByIdAsync` in case a user was deleted between the listing and this update.
- `AddToRolesAsync`: changed `.Select(u => u.RoleName)` → `.Select(u => u.RoleName!)` to resolve CS8620 (`IEnumerable<string?>` → `IEnumerable<string>`).

### 5. `PetShopWeb/Attributes/ImageExtensionAttribute.cs`
- `string path` → `string? path`: the `as string` cast can legitimately return null (e.g. if `value` is neither an `IFormFile` nor a `string`), so the local variable must be nullable. The existing null check on `path` below already handled this correctly at runtime; this change makes the compiler aware.

---

## Next Steps
None — the project builds cleanly with zero errors and zero warnings on net10.0.

The application uses:
- ASP.NET Core MVC on net10.0
- EF Core 10 with SQL Server and lazy loading proxies
- ASP.NET Core Identity with role management
- All modern .NET 10 patterns (minimal hosting, top-level route registration)

The only runtime requirement is a reachable SQL Server instance configured via `ConnectionStrings:DefaultConnection` in `appsettings.json`. The containerised connection string is already present in the file (commented out as an alternative).
