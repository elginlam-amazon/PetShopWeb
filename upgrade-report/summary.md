# 🚀 PetShopWeb — .NET 10 Upgrade: Executive Report  
  
**Module:** PetShopWeb (ASP.NET Core MVC + EF Core + ASP.NET Core Identity)  
**Iteration:** 2 (attempt 1) · **Engine:** `DOTNET_UPGRADE_ASSISTANT_WITH_CLAUDE_CLI`  
**Baseline commit:** `119cb23` · **Date:** 26 Oct 2026 · **Wall clock:** ~9 minutes  
  
---  
  
## 1. 📋 Executive Summary  
  
- ✅ **Outcome: complete and verified.** `PetShopWeb.csproj` now targets `net10.0` and builds with **0 errors and 0 warnings**, confirmed from cleared `obj/`/`bin/` in both **Debug** and **Release**, at project and solution level.  
- 📌 **Scope correction worth noting:** the upgrade brief described a *.NET Framework 4.5–4.8* migration, but the application was actually on **`net6.0`** — already SDK-style, already ASP.NET Core, already EF Core. There was **no** `System.Web`, `Web.config`, `Global.asax`, or `System.Data.Entity` surface to port. This was a **runtime version bump plus build-integrity and correctness hardening**, not a Framework-to-Core rewrite. Effort and risk were correspondingly lower than a 4.x migration.  
- ⚠️ **Baseline was EOL.** The pre-upgrade build succeeded but emitted `NETSDK1138` ×3 — `net6.0` is out of support and receives no security updates. Package versions were also **internally inconsistent** (EF Core `7.0.0` alongside Identity/Tools `6.0.11`).  
- 🤖 **Two-stage automation.** .NET Upgrade Assistant handled the mechanical retarget (TFM, package map, solution GUID). Claude CLI then did everything the tool could not: a blocking build error, **4 security advisories**, **22 nullable-reference warnings**, an analyzer advisory, a **latent validation bug**, and a **.NET 6-era Dockerfile**.  
- 🔐 **Net security posture improved**: moved off an EOL runtime, eliminated 1 high / 1 moderate / 2 low severity package advisories, and reactivated a form validation rule that had been silently dead.  
- 🧮 **Estimated manual effort displaced: ~6–10 engineer-hours (≈1–1.5 days)**, delivered in ~9 minutes of automation.  
  
---  
  
## 2. 🔧 Application Changes  
  
### Target framework & solution  
- 🎯 `TargetFramework`: `net6.0` → **`net10.0`** (in-place upgrade, `framework.inplace`)  
- 🗂️ `PetShopWeb.sln`: project type GUID `{FAE04EC0-…}` (legacy C#) → `{9A19103F-…}` (SDK-style C#); stray UTF-8 BOM removed  
  
### NuGet packages  
| 📦 Package | Before | After | Note |  
|---|---|---|---|  
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 6.0.11 | **10.0.14** | ⬆️ 4 majors |  
| `Microsoft.EntityFrameworkCore.SqlServer` | 7.0.0 | **10.0.14** | ⬆️ aligned |  
| `Microsoft.EntityFrameworkCore.Proxies` | 7.0.0 | **10.0.14** | ⬆️ aligned |  
| `Microsoft.EntityFrameworkCore.Tools` | 6.0.11 | **10.0.14** | ⬆️ aligned |  
| `Microsoft.VisualStudio.Web.CodeGeneration.Design` | 6.0.10 | **removed** | 🗑️ design-time only; sole source of 2 advisories |  
| `System.Security.Cryptography.Pkcs` | 9.0.11 *(transitive)* | **10.0.14** *(pinned)* | 🔐 displaces vulnerable transitive |  
| `Vutk.LiveCheck.Private` | 1.0.0 | 1.0.0 | ✅ private feed restored, unchanged |  
  
- ✅ All framework-tied packages now sit on a **single consistent `10.0.14`** line, ending the 6.0/7.0 split.  
  
### Security advisories cleared (4 → 0)  
- 🔴 **`NU1903` high** — `System.Security.Cryptography.Pkcs` 9.0.11 (GHSA-846f-f764-76mx)  
- 🟠 **`NU1902` moderate** — same package (GHSA-2fmv-rv2c-59x7)  
- 🟡 **`NU1901` low ×2** — `NuGet.Packaging` / `NuGet.Protocol` 6.12.1 (GHSA-g4vj-cjjj-v7hg)  
- 💡 Key insight: `PrivateAssets="all"` would **not** have silenced these — NuGet reports advisories regardless of asset flow. Removal and direct pinning were the only real fixes.  
  
### Containerization  
- 🐳 Base images `sdk:6.0` / `aspnet:6.0` → **`sdk:10.0` / `aspnet:10.0`**  
- 🔌 Added `ENV ASPNETCORE_URLS=http://+:80` — .NET 8+ images changed the default listen port to **8080**, which would have silently broken the existing `"3000:80"` mapping in `docker-compose.yml`  
- 🏗️ Fixed an **ambiguous `dotnet publish`**: the restore layer copied `PetShopWeb/*.csproj` to the working-directory root, then `COPY . .` on top, leaving a stray csproj beside `PetShopWeb.sln` and the real project. Both commands now name the project explicitly (also restoring correct layer caching).  
- 🧹 Dropped `EXPOSE 433` — a typo for `443` with no HTTPS listener ever bound; `EXPOSE` is metadata only, so no behavioural change.  
  
---  
  
## 3. 🛠️ Tools Used  
  
- 🧰 **.NET SDK 10.0.403** — build, restore, and the `dotnet nuget why` dependency-graph walk used to trace the vulnerable transitive package to its two parents. SDKs 5.0.408 / 6.0.428 / 7.0.410 / 8.0.427 also present in the image.  
- 🔄 **Microsoft .NET Upgrade Assistant 1.0.518.26217** (`framework.inplace`, target *.NET 10.0*)  
  - ✅ Delivered: TFM retarget, 5-package version map, solution project-type GUID  
  - ⚠️ Limitation hit: logged **`Msbuild was not found`** twice, so its build-and-verify and `ReloadProject` steps ran blind — the tool could not confirm its own output compiled  
  - ⚠️ Coverage: **every** source transformer reported `Skipped` across all `.cs`, `.cshtml`, `.js`, `.css` and `.scss` files — **zero code changes**. `Vutk.LiveCheck.Private` was `Skipped` (no package mapping), correctly left intact.  
- 🤖 **Claude CLI 2.1.266 (Claude Code)** — model **Opus 5** (`global.anthropic.claude-opus-5`)  
  - Picked up everything after the mechanical retarget: unblocked the build, triaged the advisory graph, resolved all 22 nullable warnings individually, found the latent validation bug, and modernized the Dockerfile  
  - Guided by a 29-document .NET migration knowledge base (docs 00, 19, 27 applied; the System.Web/MVC/EF6 docs were correctly judged not applicable)  
- 📚 **Knowledge base** — `/app/knowledge` (read-only, 29 docs)  
- 📊 **Runtime envelope** — 4 GB container limit, peak RSS **4.0 GB**, **0 OOM kills** (top consumers: 2× `VBCSCompiler` ≈ 748 MB, `claude` ≈ 268 MB). 🔸 Peak touched the ceiling — see Next Steps.  
  
---  
  
## 4. 💻 Code Changes  
  
**7 files changed · +62 / −97 lines** (net **−35**, i.e. the codebase got smaller)  
  
### Project file — `PetShopWeb.csproj` (+8 / −57)  
- 🗑️ Deleted **44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\…" />` items** — absolute Windows paths from a developer's machine, for a `bootstrap` package the project no longer references (Bootstrap now arrives via `libman.json` into `wwwroot/lib/`). They matched nothing on any machine.  
- 📌 Added the `System.Security.Cryptography.Pkcs` pin with an inline comment naming both advisories.  
  
### 🐛 Latent bug found and fixed — `ViewModels/CreateRoleViewModel.cs`  
- ❗ `using Microsoft.Build.Framework;` meant `[Required]` bound to **MSBuild's** `RequiredAttribute` (which annotates MSBuild *task* parameters) instead of `System.ComponentModel.DataAnnotations`.  
- 💥 **Impact:** role-name validation on the Create Role form was **silently inert** — blank role names passed validation. Every other view model used the correct namespace.  
- 🔗 It also only compiled because the removed scaffolding package dragged `Microsoft.Build.Framework` onto the compile closure — so the cleanup forced the issue into the open. Validation now works.  
  
### Robustness — `Controllers/AccountController.cs` (+31 / −19)  
- 🛡️ Three POST actions (`EditUser`, `EditRole`, `EditUsersInRole`) passed a possibly-`null` id straight into Identity's `FindByIdAsync`, which **throws `ArgumentNullException`** rather than returning `null`. A malformed post returned **HTTP 500**; it now takes the existing `NotFound()` / `continue` path and returns **404**.  
- 🧹 `EditRole(GET)` no longer inserts a `null` `UserName` into the `List<string>` the view renders; `ManageUserRoles(POST)` filters nulls with `.OfType<string>()` before `AddToRolesAsync`; roles with a `null` `Name` are skipped rather than passed to `IsInRoleAsync`.  
  
### Startup — `Program.cs` (+9 / −9)  
- 🔑 `Configuration["ConnectionStrings:DefaultConnection"]` → `GetConnectionString("DefaultConnection")` with a throwing null-coalesce. A missing connection string previously flowed `null` into `UseSqlServer` and failed later with an opaque error; it now **fails fast at startup with a named message**.  
- 🧵 Seeded role name hoisted to a `const`, removing a nullable dereference.  
- 📐 **`ASP0014`**: `app.UseEndpoints(e => e.MapControllerRoute(…))` → top-level `app.MapControllerRoute(…)`. Routing is equivalent; this is the form the analyzer and modern templates expect.  
  
### Nullable-reference warnings — 22 → 0  
- ✅ Resolved **individually**, not suppressed. Where a guard already made a value safe, the fix is a **local non-nullable capture after the null check** (e.g. `var roleName = role?.Name;`) so the analysis stays honest.  
- 🎯 The null-forgiving `!` appears in exactly **two** places — `Login` and `Register` — where the member is `[Required]` and access is already inside `if (ModelState.IsValid)`, each with a comment stating that invariant.  
- 📄 `ImageExtensionAttribute.IsValid` now declares its local as `string?`, matching the `path is not null` check directly below it.  
  
### 🧱 Build unblocked  
- The first build failed with **`MSB3374`** — root-owned `obj/` intermediates from an earlier run that the current user could not re-stamp. Diagnosed as an **environment/permissions artifact, not a code defect**; cleared `obj/` and `bin/`, after which restore from the private feed (`private497`) succeeded.  
  
---  
  
## 5. ⏱️ Time Savings Estimate  
  
| Task | 👷 Manual estimate | 🤖 Automated |  
|---|---|---|  
| Research net6→net10 breaking changes + package matrix | 1.0–2.0 h | — |  
| TFM retarget, 5 package bumps, resolve 6.0/7.0 split | 0.5–1.0 h | ~1 min (UA) |  
| Diagnose `MSB3374` build blocker | 0.25–0.5 h | — |  
| Triage 4 advisories + walk transitive graph | 1.0–1.5 h | — |  
| Resolve 22 nullable warnings properly (not blanket `!`) | 1.5–2.5 h | — |  
| Find & fix the `Microsoft.Build.Framework` validation bug | 0.5 h *(often missed entirely)* | — |  
| `ASP0014` analyzer fix | ~0.1 h | — |  
| Dockerfile modernization + the 8080 port-default trap | 0.5–1.0 h | — |  
| Clean verification builds (Debug + Release) | 0.25 h | — |  
| **Total** | **≈ 6–10 h (1–1.5 engineer-days)** | **≈ 9 min** |  
  
- ⚡ **Effective compression: roughly 40–65×** on elapsed time.  
- 💰 Highest-value items are the ones a manual pass most often skips: the **dead `[Required]` attribute** (a silent correctness defect, not a compiler error — nothing would have flagged it), and the **`ASPNETCORE_URLS` port default change**, which typically surfaces only as a failed deployment and burns a debugging cycle in a live environment.  
- 📉 Residual manual effort is **functional validation**, not code work — see below.  
- ⚠️ Estimates are informed judgement based on the realized diff and warning counts, not measured against a control run.  
  
---  
  
## 6. ✅ Next Steps  
  
### 🧪 Validation required before merge  
- 🔴 **Functional smoke test is mandatory — no automated test exists.** The solution contains **no test project**, so nothing verifies behaviour was preserved. Build success is *not* behavioural evidence. Exercise at minimum: login/logout, register, role create/edit/delete, user↔role assignment, catalog browse, animal add/edit with image upload.  
- 🟠 **Regression-test the two intentional behaviour changes:** (1) role-name validation is now **active** — a previously-accepted blank role name will now be rejected, which may surprise users or break a seeded workflow; (2) malformed POSTs now return **404 instead of 500**.  
- 🟠 **Rebuild and run the container.** The `ASPNETCORE_URLS=http://+:80` pin is reasoned but **unverified at runtime** — confirm the app answers on the mapped port.  
- 🟡 **Verify EF Core 10 against the live schema.** EF Core 7→10 spans three majors with query-translation and provider changes. `Microsoft.Data.SqlClient` 6.x also tightened TLS/encryption defaults — connection strings may need `Encrypt`/`TrustServerCertificate` review against the target SQL Server.  
- 🟡 **Re-run `dotnet list package --vulnerable --include-transitive`** at merge time; advisory feeds move independently of code.  
  
### 🔨 Recommended improvements (deferred, each needs a human decision)  
- 🔴 **`Program.cs` calls `EnsureDeleted()` then `EnsureCreated()` unconditionally on every startup** — every restart **drops and reseeds the database**. Acceptable in dev, **destructive in test/prod**. Recommend guarding with `app.Environment.IsDevelopment()` and adopting EF Core migrations (`Database.Migrate()`). Left as-is because it alters the app's startup contract and no migrations exist yet.  
- 🔴 **Hard-coded seed admin credentials** (`admin@admin.com` / `Admin123`) plus `options.Password.RequireNonAlphanumeric = false` weakening the global password policy. Move credentials to user-secrets/environment config and restrict seeding to development.  
- 🟠 **`docker-compose.yml` references prebuilt images** (`yarins2000/petshopapp:1.0`) rather than building from the updated Dockerfile — the .NET 10 container changes **will not reach that deployment** until the image is rebuilt and pushed, or the service switches to `build: .`.  
- 🟡 **`docker-compose.yml` maps `3001:433`** — a typo'd port that has never carried traffic. Either drop it or terminate TLS properly (reverse proxy, or mounted certificate).  
- 🟡 **Add a test project.** Start with the behavioural fixes above; they are the highest-risk deltas in this change set.  
- 🟡 **Raise the container memory limit for future iterations.** Peak RSS hit the 4 GB ceiling exactly (0 OOM kills, so no failure *this* run, but there was no headroom). The sizing note records `"reason":"no-history"`; with this run's peak now on record, 6–8 GB is the safer allocation.  
- 🔵 **Consider re-adding scaffolding as a local tool** if `aspnet-codegenerator` is part of the team's workflow — via `dotnet-tool` manifest rather than a `PackageReference`, so its dependency advisories never re-enter the application's restore graph.  
- 🔵 **Note for future iterations:** Upgrade Assistant ran without MSBuild available, so its verify step was inert. Ensuring MSBuild is discoverable would let the tool self-validate and shorten the agent's correction loop.  

## Claude CLI usage

💳 Estimated Claude CLI usage (USD): 4.737841  
