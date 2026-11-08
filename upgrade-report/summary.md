# 🚀 Executive Report: PetShopWeb .NET 6 → .NET 10 Upgrade  
  
**Project:** PetShopWeb (ASP.NET Core MVC · Identity · EF Core · Repository Pattern)  
**Upgrade path:** `net6.0` → `net10.0`  
**Date:** Sun, 8 Nov 2026  
**Final build status:** ✅ `0 errors · 0 warnings`  
  
---  
  
## 📋 Executive Summary  
  
The PetShopWeb application was successfully upgraded from .NET 6 to .NET 10 in a fully automated, single-iteration pipeline. The upgrade was executed without manual developer intervention, combining Microsoft's .NET Upgrade Assistant for project and package scaffolding with Kiro (AI-powered code intelligence) for correctness fixes, security remediation, and code quality improvements. The application — an ASP.NET Core MVC pet shop with ASP.NET Identity, Entity Framework Core, and a repository pattern — compiled cleanly with zero errors and zero warnings at completion. All business logic, authentication flows, and data access patterns were preserved unchanged.  
  
---  
  
## 🗂️ Application Changes  
  
- 🏗️ **Project type:** SDK-style `.csproj` — already in place from a prior migration; retained and cleaned  
- 🎯 **Target framework:** `net6.0` → `net10.0` (in-place upgrade)  
- 📦 **NuGet packages upgraded:** 5 direct dependencies bumped to their .NET 10 equivalents  
- 🔒 **Security vulnerability resolved:** Transitive `System.Security.Cryptography.Pkcs` 9.0.11 (high + moderate CVEs) overridden to 10.0.14  
- 🧹 **Project file cleaned:** 46 stale Windows-machine-specific `<Content Remove>` entries removed (Bootstrap NuGet artefacts superseded by LibMan)  
- 🐳 **Dockerfile updated:** Base images upgraded from `sdk:6.0`/`aspnet:6.0` → `sdk:10.0`/`aspnet:10.0`; port corrected from `EXPOSE 80` to `EXPOSE 8080` (ASP.NET Core non-root default); erroneous `EXPOSE 433` removed  
- ⚙️ **Startup modernised:** `Program.cs` updated to use `app.MapControllerRoute()` (replacing legacy .NET 5 `UseEndpoints` wrapper), typed `GetConnectionString()` with fail-fast null guard, and blocking `GetAwaiter().GetResult()` seed calls converted to `await`  
- 🐛 **Correctness bug fixed:** `CreateRoleViewModel` was importing `[Required]` from `Microsoft.Build.Framework` (an MSBuild task attribute) — silently bypassing server-side model validation; corrected to `System.ComponentModel.DataAnnotations`  
- 🔧 **Nullable safety hardened:** 14 CS8604/CS8620 warnings in `AccountController.cs` resolved (Identity `IdentityRole.Name` / `IdentityUser.UserName` became `string?` in newer SDK); 1 CS8600 in `ImageExtensionAttribute.cs` corrected  
- 📊 **Audit noise suppressed:** `<NuGetAuditLevel>moderate</NuGetAuditLevel>` added to filter low-severity advisories confined to the design-time scaffolding tool's private-asset closure (no runtime impact)  
  
---  
  
## 🛠️ Tools Used  
  
| Tool | Version / Detail |  
|---|---|  
| ⚙️ **dotnet SDK** | .NET 10 SDK (`dotnet build`) — compilation, restore, vulnerability audit |  
| 🔄 **Microsoft .NET Upgrade Assistant** | v1.0.518.26217 — TFM bump (`net6.0 → net10.0`), package version mapping, solution file project-type GUID update |  
| 🤖 **Kiro CLI** | v2.35.1 — AI-assisted code analysis, bug detection, security remediation, nullable hardening, modernisation |  
| 🧠 **Kiro AI Model** | `claude-sonnet-4.6` — reasoning engine for code understanding, fix generation, and dependency graph analysis |  
  
---  
  
## 💻 Code Changes  
  
### `PetShopWeb.csproj`  
- ➕ Added `System.Security.Cryptography.Pkcs` 10.0.14 (explicit override to eliminate high/moderate CVEs from transitive chain via `Microsoft.Data.SqlClient`)  
- ➕ Added `<NuGetAuditLevel>moderate</NuGetAuditLevel>` property  
- 🗑️ Removed 46 Windows-path `<Content Remove>` entries for Bootstrap (now managed by LibMan)  
- 🔼 All 5 package references bumped to `10.x` by Upgrade Assistant  
  
### `Program.cs`  
- 🔄 `app.UseEndpoints(e => { e.MapControllerRoute(...) })` → `app.MapControllerRoute(...)`  
- 🔄 `builder.Configuration["ConnectionStrings:DefaultConnection"]` → `GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(...)`  
- 🔄 `.GetAwaiter().GetResult()` blocking seed calls → `await` (top-level async)  
  
### `ViewModels/CreateRoleViewModel.cs`  
- 🐛 `using Microsoft.Build.Framework;` → `using System.ComponentModel.DataAnnotations;`  
  - *(Bug: `[Required]` was an MSBuild task attribute, not a validation attribute — model validation never fired)*  
  
### `Controllers/AccountController.cs`  
- ✅ Applied `!` null-forgiving operators on `[Required]`-guarded properties (`loginmodel.Email!`, `registermodel.Password!`)  
- ✅ Used `?? string.Empty` for nullable `IdentityRole.Name` / `IdentityUser.UserName` passed to Identity APIs  
- ✅ Added explicit `if (user is null) continue;` guard in `EditUsersInRole`  
- ✅ Fixed CS8620: `model.Where(x => x.RoleName != null).Select(u => u.RoleName!)` before `AddToRolesAsync`  
  
### `Attributes/ImageExtensionAttribute.cs`  
- 🔧 `string path` → `string? path` for `value as string` assignment (CS8600 fix)  
  
### `Dockerfile`  
- 🐳 `sdk:6.0` → `sdk:10.0`, `aspnet:6.0` → `aspnet:10.0`  
- 🔧 `EXPOSE 80` → `EXPOSE 8080`, removed `EXPOSE 433`  
- 🔧 Fixed `COPY` path to resolve correctly from repo root  
  
---  
  
## ⏱️ Time Savings Estimate  
  
| Activity | Manual Estimate | Automated |  
|---|---|---|  
| TFM bump + package version research & update | 1.5 – 2 hrs | ✅ ~30 sec (Upgrade Assistant) |  
| Dependency vulnerability audit & override research | 1 – 2 hrs | ✅ ~2 min (Kiro + dotnet audit) |  
| Nullable reference warning triage & fix (14 warnings across Identity APIs) | 1 – 1.5 hrs | ✅ ~1 min (Kiro) |  
| Correctness bug detection (`[Required]` wrong namespace) | Days – never found without testing | ✅ Caught statically |  
| `Program.cs` modernisation + startup pattern review | 0.5 – 1 hr | ✅ ~30 sec (Kiro) |  
| Dockerfile update + port/image research | 0.5 hr | ✅ ~30 sec (Kiro) |  
| `.csproj` cleanup (46 stale entries) | 0.5 hr | ✅ ~30 sec (Kiro) |  
| **Total** | **~5.5 – 8.5 hours** | **~5 minutes** |  
  
> 💡 **Estimated time saving: 5–8 hours** of developer time for a project of this scale and complexity. The hidden correctness bug (`[Required]` namespace) alone could have taken significant debugging time to surface without AI-assisted static analysis.  
  
---  
  
## 🔜 Next Steps  
  
### ✅ Validation  
  
- 🧪 **Run integration / smoke tests** against a dev SQL Server instance to verify Identity flows (login, register, role management) and EF Core seed data operate correctly on .NET 10  
- 🗄️ **Update connection string** in `appsettings.json`: the active string targets Windows-only `(localdb)\AspNetCoreCourse`; for Linux/container deployment use the Docker/SQL Server connection string (commented out in `appsettings.json`) and inject credentials via environment variables or a secrets manager  
- 🐳 **Validate the updated Dockerfile** end-to-end with `docker build` + `docker run` to confirm the container starts and serves requests on port 8080  
  
### ⚠️ Known Risks / Improvement Recommendations  
  
- 🗑️ **Remove `ctx.Database.EnsureDeleted()`** from `Program.cs` before any persistent deployment — the database is currently dropped and recreated on every startup (acceptable for demo, destructive in production)  
- 🔑 **Externalise the seed admin password** — `"Admin123"` is hardcoded in `Program.cs`; inject it from a secrets manager or `IConfiguration` before going to production  
- 🔒 **Add `app.UseHttpsRedirection()`** to the middleware pipeline for environments where HTTPS is terminated at the app layer rather than a reverse proxy  
- 📦 **`NuGet.Packaging`/`NuGet.Protocol` low-severity advisory** (GHSA-g4vj-cjjj-v7hg) is suppressed via `<NuGetAuditLevel>moderate</NuGetAuditLevel>` — these packages live only inside the design-time scaffolding tool's private asset closure and have zero runtime impact; will self-resolve when Microsoft ships an updated `Microsoft.VisualStudio.Web.CodeGeneration.Design` (currently at latest 10.0.2)  
- 🌐 **LibMan restore in CI/CD** — add a `libman restore` step before `dotnet publish` in any pipeline that runs in a clean environment without a pre-populated `wwwroot/lib` (Bootstrap, jQuery, etc.)  

## Kiro credit usage

💳 Kiro CLI credits used: 13.81  
Includes 0.47 for job setup (startup smoke test and knowledge-base indexing).  
