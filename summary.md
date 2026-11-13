# PetShopWeb — .NET 6.0 → .NET 10.0 Migration Summary

**Target project:** `./PetShopWeb/PetShopWeb.csproj`
**Target framework:** `net10.0`
**Final build state:** `dotnet build` succeeds in **Debug and Release** with **0 errors, 0 warnings**.

---

## Starting state

The project was already an ASP.NET Core MVC application (not .NET Framework / Web Forms),
so no `System.Web`, `System.Data.Entity`, `Global.asax`, `Web.config`, WCF, ASMX, SignalR 2,
System.Drawing, bundling or OWIN migration was required — the repository contains none of
these. A scan confirms zero remaining references to `System.Web.*`, `System.Data.Entity`
or `System.Web.Http.*`.

`PetShopWeb.csproj` had already been retargeted to `net10.0` with `10.0.15` package
versions. The build nevertheless failed and then produced 23 warnings; both were fixed.

---

## Changes made

### 1. Build failure: stale `obj/` artifacts (`MSB3374`)
The checked-in `obj/` and `bin/` trees contained root-owned intermediate files from a
previous build. The Razor `MvcApplicationPartsDiscovery` target could not stamp
`PetShopWeb.MvcApplicationPartsAssemblyInfo.cache`, failing the build with
`error MSB3374: ... Access to the path ... is denied`.

**Fix:** removed the stale `PetShopWeb/obj` and `PetShopWeb/bin` directories so the SDK
regenerates them. No source change; nothing in these directories is source of record.

### 2. `PetShopWeb.csproj` cleanup
- **Removed 44 bogus `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">`
  items.** These were machine-specific Windows absolute paths left over from a local
  NuGet-based Bootstrap reference on a developer's box. The project no longer references
  the `bootstrap` NuGet package (Bootstrap is vendored under `wwwroot/lib` via
  `libman.json`), so the items matched nothing and would never resolve on Linux.
- **Removed `<Folder Include="wwwroot\photos\" />`.** This item only exists to make Visual
  Studio show an empty folder; `wwwroot/photos` is populated with the seeded animal images,
  so the item is redundant (and used a Windows path separator).
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design` 10.0.3.** This is a
  design-time scaffolding-only package. It dragged in `NuGet.Packaging` /
  `NuGet.Protocol` 6.12.1, which are the sole cause of the two `NU1901` advisory warnings
  (GHSA-g4vj-cjjj-v7hg). It contributes nothing at runtime and nothing in the application
  depends on it. Per KB `27-blocked-packages-and-build-integrity.md` (prefer removal over
  silencing), the reference was deleted. Scaffolding on .NET 10 is done with the
  `dotnet-scaffold` global tool, which does not need a project reference.
- `Microsoft.EntityFrameworkCore.Tools` was kept with its existing
  `PrivateAssets`/`IncludeAssets` metadata (needed for `dotnet ef` migrations).

### 3. `Program.cs`
- **`ASP0014`** — replaced the `app.UseEndpoints(endpoints => endpoints.MapControllerRoute(...))`
  block with the top-level `app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}")`
  registration. Routing behaviour is identical; `UseEndpoints` is the discouraged form on
  modern ASP.NET Core.
- **`CS8600`** — `builder.Configuration["ConnectionStrings:DefaultConnection"]` returns
  `string?` and was assigned to a non-nullable `string`. Replaced with the standard
  template pattern:
  `builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException(...)`.
  **Behavioural note:** a missing connection string now fails fast at startup with a clear
  message instead of being passed as `null` to `UseSqlServer`, which previously produced an
  obscure failure on first database access.
- **`CS8604`** — the admin role name is now a `const string adminRoleName = "admin"`, used
  for both `new IdentityRole(adminRoleName)` and `AddToRoleAsync(adminUser, adminRoleName)`,
  removing the dereference of the nullable `IdentityRole.Name`. Same value, same behaviour.
- Dropped the unused `result` local from the admin-user seeding call (the value was never
  inspected).

### 4. `ViewModels/CreateRoleViewModel.cs` — real validation bug fixed
The file imported **`Microsoft.Build.Framework`**, so its `[Required]` attribute bound to
`Microsoft.Build.Framework.RequiredAttribute` (an MSBuild task-parameter marker) rather
than `System.ComponentModel.DataAnnotations.RequiredAttribute`. It compiled only because
the scaffolding package transitively supplied MSBuild assemblies, and it meant
**`CreateRoleViewModel.RoleName` was never actually validated** — `ModelState.IsValid` was
true for an empty role name.

**Fix:** changed the using to `System.ComponentModel.DataAnnotations`.
**Behavioural note:** creating a role with a blank name is now correctly rejected by model
validation and redisplays the form, instead of being submitted to `RoleManager`. This is
the originally intended behaviour.

### 5. `Attributes/ImageExtensionAttribute.cs`
**`CS8600`** — `path` was declared `string` but assigned from `value as string`, which can
be `null`. Declared it `string?`; the existing `if (path is not null)` guard already
handles the null case, so validation behaviour is unchanged.

### 6. `Controllers/AccountController.cs` — 17 nullable-reference warnings
All fixes preserve the existing control flow.

- **Validated view-model properties** (`CS8604`): `loginmodel.Email`/`Password` and
  `registermodel.Password` are dereferenced inside `if (ModelState.IsValid)` and carry
  `[Required]`, so they are non-null at that point. Marked with the null-forgiving
  operator (`!`) rather than adding redundant runtime checks.
- **Unvalidated identifiers from the form** (`CS8604`): `EditUser(EditUserViewModel)` and
  `EditRole(EditRoleViewModel)` passed a nullable `model.Id` straight into
  `FindByIdAsync`. They now short-circuit — `model.Id is null ? null : await FindByIdAsync(model.Id)`
  — which lands on the already-present `if (... is null) return NotFound()` branch.
  Identical observable behaviour, minus the potential `ArgumentNullException`.
- **`IdentityRole.Name` / `IdentityUser.UserName`** (`CS8604`): these are `string?` on the
  Identity model types, but an entity loaded from the store always has them set. In
  `EditRole`, `EditUsersInRole` and `ManageUserRoles` the name is now hoisted once into a
  local (`var roleName = role.Name!;`) after the `null` check on the entity and reused,
  which both clears the warnings and removes repeated property access inside the user loops.
- **`EditUsersInRole` POST** (`CS8604`): `FindByIdAsync(model[i].UserId)` could be called
  with a null id and the resulting possibly-null `user` was passed to `IsInRoleAsync` /
  `AddToRoleAsync`. The loop now skips entries with a null `UserId` or an unresolvable
  user. **Behavioural note:** previously such an entry threw `ArgumentNullException` and
  returned a 500; it is now skipped and the remaining entries are still processed. The
  `IdentityResult? result` local became non-nullable `IdentityResult`, since every path
  that reaches its use assigns it.
- **`ManageUserRoles` POST** (`CS8620`): `AddToRolesAsync` was handed an
  `IEnumerable<string?>` from `.Select(u => u.RoleName)`. Now filtered —
  `.Where(x => x.IsSelected && x.RoleName is not null).Select(u => u.RoleName!)` — so a
  selected row with no role name is dropped instead of sending a null into Identity.

---

## 7. Container and hosting stack — still pinned to .NET 6 (fixed this cycle)

The repository root contains a `Dockerfile` and `docker-compose.yml` that had been missed
in earlier cycles. They still targeted .NET 6, which meant the **upgraded `net10.0`
application could not run in its own container at all** — a `net10.0` publish cannot
execute on the `aspnet:6.0` runtime image. Fixed per KB `19-linux-containerization.md`.

### `Dockerfile`
- **Base images `sdk:6.0` → `sdk:10.0` and `aspnet:6.0` → `aspnet:10.0`.** The hard blocker.
- **Fixed an ambiguous publish that would have failed the image build.** The old file did
  `COPY PetShopWeb/*.csproj .` (landing the csproj at `/app`) and then `COPY . .` (landing
  `PetShopWeb.sln` alongside it), so the bare `dotnet restore` / `dotnet publish` ran in a
  directory holding **both** a project and a solution file — MSBuild errors with `MSB1011`
  ("more than one project or solution file"). The project path is now explicit in both
  commands, and the restore layer copies the csproj to its real relative path so Docker
  layer caching for `dotnet restore` actually works.
- **Container port `80` → `8080`, with `ASPNETCORE_URLS=http://+:8080`.** This is a genuine
  .NET 6 → .NET 8+ breaking change: the ASP.NET Core container images changed their default
  listening port from 80 to 8080. Leaving the published port at 80 would have produced a
  container that starts but serves nothing on the mapped port.
- **Added `USER $APP_UID`** so the app runs as the non-root `app` user the .NET 8+ images
  provide. This is only possible because the port moved above 1024 — the two changes go
  together.
- **Dropped `EXPOSE 433`.** It was a typo for 443, and nothing was listening there: the
  container configures no HTTPS certificate. HTTPS should be terminated at the reverse
  proxy, or a certificate mounted per KB `19` Step 7.

### `.dockerignore` (new)
Added so `COPY . .` no longer copies the local `bin/` and `obj/` trees into the build
context. Beyond bloating the context, those directories contain host build artifacts
(including root-owned intermediates — see item 1) that can collide with the in-container
restore and publish.

### `docker-compose.yml`
- **The `app` service now has `build: .`** instead of only pulling
  `yarins2000/petshopapp:1.0`. That published image is the **old .NET 6 build**, so
  `docker-compose up` would have kept running pre-upgrade code no matter what was in the
  repository. The built image is tagged `:2.0` to avoid overwriting the published 1.0 tag.
- **Published port `3000:80` → `3000:8080`** to match the new container port. The external
  port 3000 from the README is unchanged.
- **Removed the obsolete top-level `version: '3.3'` key**, which current Compose warns on.
- **Supplies the connection string via `ConnectionStrings__DefaultConnection`** (see item 8).
- Dropped the `3001:433` mapping along with the typo'd `EXPOSE`.

## 8. Per-environment connection string (carry-over item, resolved)

`appsettings.json` shipped an active `Server=(localdb)\AspNetCoreCourse;Trusted_Connection=True`
connection string — Windows LocalDB, which cannot work on Linux or in the container — with
the container-appropriate `Server=tcp:db,1433` value sitting **commented out directly above
it**, including an `sa` password in source. `appsettings.Development.json` had no
`ConnectionStrings` section at all.

Configuration now resolves per environment, using the standard precedence
(`appsettings.json` → `appsettings.{Environment}.json` → environment variables):

| Environment | Source of `DefaultConnection` |
|---|---|
| Development | `appsettings.Development.json` → LocalDB (a developer-machine convenience) |
| Container / Compose | `ConnectionStrings__DefaultConnection` env var → `Server=tcp:db,1433` |
| Staging / Production | **must** be supplied by the environment or a secret store |

- `appsettings.json` no longer contains a `ConnectionStrings` section, which also removed
  the committed `sa` password from the commented-out line.
- Because nothing is baked into the base file, a Staging/Production host that fails to
  supply the value now **fails fast at startup** with the clear
  `InvalidOperationException` added in item 3, rather than silently passing `null` to
  `UseSqlServer` and failing obscurely on first query.

**Behavioural note:** running the app with `ASPNETCORE_ENVIRONMENT` unset (i.e. Production)
and no environment variable set will now throw at startup instead of starting and failing
later. This is intentional — a missing database connection string is a configuration error.

`README.md` was updated for the new build/run flow, the 8080 container port, and the
`.NET 10` version reference in its header.

---

## Verification

| Check | Result |
|---|---|
| `dotnet build PetShopWeb/PetShopWeb.csproj` (Debug) | succeeded — 0 errors, 0 warnings |
| `dotnet build PetShopWeb.sln -c Release` | succeeded — 0 errors, 0 warnings |
| `dotnet publish PetShopWeb/PetShopWeb.csproj -c Release` (the exact command the Dockerfile runs) | succeeded — produces `PetShopWeb.dll` + both `appsettings` files |
| Residual `System.Web.*` / `System.Data.Entity` / `System.Web.Http.*` references | none |
| Unit tests | **no test project exists in this solution** — nothing to run |

The application was not started, per the instruction not to execute the built app.
**The container images were not built or run** — Docker is not available in this
environment, so the `Dockerfile` and `docker-compose.yml` changes are verified by
inspection plus a local `dotnet publish` of the same project path and command the
Dockerfile uses. The YAML was checked for tab characters and consistent nesting. Building
the image is listed under validation below.

---

## Next steps (non-blocking notes for the next cycle / human review)

1. **No automated tests.** The solution has no test project, so the migration is verified
   by compilation only. The nullable-reference fixes in `AccountController` were chosen to
   preserve control flow, but the user/role management flows (`EditUsersInRole`,
   `ManageUserRoles`, `EditRole`, `EditUser`) have no coverage. Recommend adding an
   xUnit/MSTest project with `Microsoft.AspNetCore.Mvc.Testing` and in-memory Identity
   stores before further refactoring. See KB `24-test-stack-migration.md`.
2. **Role-name validation is now enforced** (item 4 above). If any existing workflow or
   screen relied on submitting a blank role name, it will now be rejected. Worth a quick
   product confirmation, though the previous behaviour was almost certainly unintended.
3. **`Program.cs` destroys and recreates the database on every startup.** The seeding block
   calls `ctx.Database.EnsureDeleted()` followed by `EnsureCreated()` unconditionally — so
   all data is wiped each time the app starts, in every environment including production.
   This predates the migration and was left untouched because changing it alters runtime
   behaviour well beyond the upgrade's scope, but it should not ship as-is. Recommended:
   guard it with `if (app.Environment.IsDevelopment())`, or move to EF Core migrations with
   `ctx.Database.Migrate()`. Needs a human decision.
4. **Seeding dead branch.** `if (!ctx.Users.Any(u => u.UserName == "Admin"))` checks for the
   user name `"Admin"`, but the user is created as `"admin@admin.com"` — so the guard never
   matches an existing user. It is currently harmless only because of the `EnsureDeleted()`
   above it; fixing item 3 would expose it. Left unchanged for the same scope reason.
5. **Hard-coded admin credentials.** `Program.cs` seeds `admin@admin.com` / `Admin123` in
   source, and `builder.Services.AddIdentity` sets
   `options.Password.RequireNonAlphanumeric = false`. Both are inappropriate for a
   production deployment. Recommend sourcing the seed credentials from configuration /
   user-secrets / a secret store and restricting the seeding to development. See KB
   `08-auth-security-migration.md`.
6. **Build the container image.** Docker is unavailable in this environment, so the
   `Dockerfile` / `docker-compose.yml` changes (item 7) were verified by inspection and by
   running the same `dotnet publish` command locally, not by building the image. Before
   merge, run `docker-compose up -d --build` and confirm the app answers on
   `localhost:3000`. Two things to watch: the `mcr.microsoft.com/dotnet/sdk:10.0` and
   `aspnet:10.0` tags must resolve in your registry/mirror, and `$APP_UID` (non-root user)
   is only defined on .NET 8+ base images — both are expected to be fine on these tags.
7. **Verify the `petshopdb` image still matches.** `yarins2000/petshopdb:1.0` is a prebuilt
   SQL Server image whose contents cannot be inspected here. The Compose connection string
   uses the `sa` / `Qwe12345!` credentials recovered from the commented-out line that was
   in `appsettings.json`, which is the author's own value for that image — but this is
   unverified without Docker. If the app cannot reach the database, this credential pair is
   the first thing to check. A deliberate decision was made **not** to inject
   `MSSQL_SA_PASSWORD` into the `db` service: SQL Server only applies that variable on
   first initialisation, so on a pre-seeded image it would silently do nothing while making
   the password look configurable. Override the whole connection string instead.
8. **The demo `sa` password is still committed**, now in `docker-compose.yml` rather than in
   `appsettings.json`. This is unchanged in substance from the original repository and keeps
   the README's one-command demo flow working, but it is not acceptable for a real
   deployment. Move it to a Compose `secret`, a `.env` file excluded from source control, or
   the platform's secret store, and override
   `ConnectionStrings__DefaultConnection` per environment.
9. **No HTTPS redirection or HSTS.** `Program.cs` calls `UseStaticFiles`, `UseAuthentication`,
   `UseRouting`, `UseAuthorization` but not `UseHttpsRedirection()`/`UseHsts()`, and the
   container now serves plain HTTP on 8080 with no certificate. That is the right shape if
   TLS is terminated at a reverse proxy or load balancer, but it needs confirming — if the
   container is ever exposed directly, Identity auth cookies would travel unencrypted. Add
   the middleware plus a mounted certificate (KB `19` Step 7) if there is no proxy.
10. **Unused `using` directives** remain in a few files (`System.Data` in
    `AccountController.cs`, `System.Security` in `EditRoleViewModel.cs`). They produce no
    warnings at the current analysis level; enabling `EnableNETAnalyzers` with
    `IDE0005` as a build warning would surface and clear them.
11. **`PetShopWeb.sln` project type GUID changed.** The solution's project GUID was
    `{FAE04EC0-...}` (legacy C#) and was rewritten by the .NET 10 SDK to
    `{9A19103F-...}` (SDK-style C#) on first build. This is a correct and expected
    normalisation, noted only so the diff is not mistaken for an unintended edit.
