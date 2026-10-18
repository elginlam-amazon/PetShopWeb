# PetShopWeb — .NET 10 (`net10.0`) Upgrade Summary

**Target:** `./PetShopWeb/PetShopWeb.csproj` → `net10.0`
**Result:** `dotnet build` succeeds with **0 errors and 0 warnings** in both `Debug` and `Release`; `dotnet publish -c Release` succeeds. No vulnerable and no deprecated packages remain in the restore graph.

The project was already SDK-style ASP.NET Core (`Microsoft.NET.Sdk.Web`, `net6.0`, `Program.cs` minimal hosting), so there was no `System.Web`, `System.Data.Entity`, `System.Web.Http`, `Global.asax`, `Web.config` or `packages.config` to migrate — the pre-build cleanup pass (KB 00/02/10/11) found nothing to remove, and this was verified by a repo-wide scan.

---

## Changes

### Project file (`PetShopWeb/PetShopWeb.csproj`) — KB 01, 06, 27
- `TargetFramework` `net6.0` → `net10.0`; EF Core / Identity packages aligned to `10.0.13`.
- **Removed `Microsoft.VisualStudio.Web.CodeGeneration.Design`.** It is a design-time scaffolding package, referenced by no code, and was the sole source of four advisories (`NU1901` ×2 on `NuGet.Packaging`/`NuGet.Protocol` 6.12.1, plus the `System.Security.Cryptography.Pkcs` chain). Per KB 27 precedence, migrating off beats keep-and-patch. Scaffolding on modern .NET is done with the `dotnet-aspnet-codegenerator` global tool instead.
- **Pinned `System.Security.Cryptography.Pkcs` to `10.0.13`** as a direct reference. `Microsoft.EntityFrameworkCore.SqlServer 10.0.13` → `Microsoft.Data.SqlClient 6.1.7` drags in `9.0.11`, which carries `NU1902` (GHSA-2fmv-rv2c-59x7, moderate) and `NU1903` (GHSA-846f-f764-76mx, high). `10.0.13` is the patched release on the target runtime major (KB 27 Step 2/3).
- Removed 44 dead `<Content Remove="C:\Users\PC\.nuget\packages\bootstrap\5.2.2\...">` items. These pointed at a hard-coded Windows user-profile path for a `bootstrap` NuGet package that is no longer referenced (client-side libraries come from `libman.json` into `wwwroot/lib`), and are meaningless on a Linux build. Also removed the redundant `<Folder Include="wwwroot\photos\" />`.
- Solution file project type GUID updated to the SDK-style GUID (`9A19103F-…`).

### `Program.cs` — KB 10, 11, 14
- `app.UseEndpoints(e => e.MapControllerRoute(...))` → top-level `app.MapControllerRoute(...)`, clearing **ASP0014**. Route template unchanged.
- Connection string read via `Configuration.GetConnectionString("DefaultConnection")` with `?? throw new InvalidOperationException(...)` instead of the indexer assigned into a non-nullable local (**CS8600**). Behaviour change: a missing connection string now fails fast at startup with a clear message rather than reaching EF Core as `null`.
- `adminRole.Name` null-forgiving at the `AddToRoleAsync` call (**CS8604**); dropped an unused `result` local.

### Nullable-reference correctness — KB 03, 08
`Nullable` was already `enable`d but the code predated it, producing 22 CS8600/CS8604/CS8620 warnings. Fixed at the source rather than suppressed:

- **View models** (`Login`, `Register`, `CreateRole`, `EditRole`, `EditUser`, `UserRole`, `UserRoles`): model-bound `string?` properties that are logically required are now non-nullable `string` with a `= string.Empty` initializer, and the collection properties are non-nullable with inline initializers (replacing the constructor-assigned `List<string>?` + `!` pattern). Validation behaviour is unchanged — `[Required]` rejects empty strings by default, exactly as it rejected `null`. `RegisterViewModel.ConfirmPassword` stays nullable (it is genuinely optional at the model level and is checked by `[Compare]`).
- **Fixed a real bug in `CreateRoleViewModel`:** `[Required]` was being resolved from `using Microsoft.Build.Framework` — MSBuild's task-parameter attribute, not `System.ComponentModel.DataAnnotations.RequiredAttribute` — so `RoleName` was **never actually validated**. It only compiled because `Microsoft.Build.Framework` arrived transitively through the scaffolding package. Corrected to the DataAnnotations namespace; role name is now genuinely required.
- **`AccountController`:** Identity-sourced `IdentityUser.UserName/Email` and `IdentityRole.Name` are null-forgiven at use (the Identity store guarantees them), preserving the previous `ArgumentNullException`-on-null contract rather than silently substituting `""`.
- **Fixed a latent `NullReferenceException` in `AccountController.EditUsersInRole` (POST):** `FindByIdAsync(model[i].UserId)` returns `IdentityUser?` and the result was passed straight to `IsInRoleAsync`/`AddToRoleAsync`, so a posted unknown/tampered user id crashed the request. Unknown ids are now skipped (`if (user is null) continue;`), consistent with how the surrounding code already handles a missing role.
- **`ImageExtensionAttribute.IsValid`:** `path` declared `string?` so the `value as string` assignment is honest (**CS8600**). The existing `if (path is not null)` guard already covered it; no behaviour change.

### Container / hosting — KB 19, 05
- `Dockerfile`: `sdk:6.0`/`aspnet:6.0` → `10.0` (the 6.0 SDK cannot build a `net10.0` app). Restore/publish now target the project explicitly — the previous `COPY PetShopWeb/*.csproj .` + bare `dotnet publish` left two project files plus the `.sln` in the build context, making the publish target ambiguous.
- Port moved to the .NET 8+ container convention: `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`. The modern `aspnet` images run as a non-root user and no longer bind port 80. `docker-compose.yml` host mapping updated to `3000:8080`; the non-functional `433` mapping (a typo for 443, with no certificate ever configured in the image) was dropped.

---

## Verification

```
dotnet restore                                    → OK
dotnet build -c Debug    → 0 Error(s), 0 Warning(s)
dotnet build -c Release  → 0 Error(s), 0 Warning(s)
dotnet publish -c Release                         → OK
dotnet list package --vulnerable --include-transitive → none
dotnet list package --deprecated --include-transitive → none
```

The application was not executed, per the project instructions. There are no test projects in the solution, so there was nothing to run under `dotnet test`.

---

## Next steps

Non-blocking items left for human review:

1. **`Program.cs` calls `ctx.Database.EnsureDeleted()` on every startup**, dropping and re-creating the database (and re-seeding the `admin@admin.com` / `Admin123` account) each time the app boots. This is pre-existing behaviour and was deliberately left untouched, but it is destructive in test and production. Recommended: guard it with `app.Environment.IsDevelopment()`, or move to EF Core migrations (`ctx.Database.Migrate()`) with the seed in a one-off initializer.
2. **The seeded admin credentials are hard-coded** (`Admin123`, with `RequireNonAlphanumeric = false`). Move to user secrets / environment variables and seed only in development.
3. **`appsettings.json` ships a `(localdb)\AspNetCoreCourse` connection string**, which is Windows-LocalDB-only and will not resolve in the Linux container. The container connection string sits commented out above it. Recommended: leave the dev value in `appsettings.Development.json` and supply the real value per environment via `ConnectionStrings__DefaultConnection` (e.g. in `docker-compose.yml`, which currently sets no environment variables for the `app` service).
4. **`docker-compose.yml` pulls prebuilt images** (`yarins2000/petshopapp:1.0`, `yarins2000/petshopdb:1.0`) rather than building from the repo, so the upgraded code only reaches the container once those images are rebuilt and republished from the updated `Dockerfile`. It also still carries the obsolete top-level `version:` key.
5. **Static assets still use `app.UseStaticFiles()`.** .NET 10 offers `MapStaticAssets()` with build-time fingerprinting and compression; adopting it requires switching the views to the `@Assets[...]` helper, so it was left out of this pass as a non-essential change.
6. **Middleware order:** `UseAuthentication()` currently runs before `UseRouting()`. This works correctly (`UseAuthorization()` is after `UseRouting()`), but the documented order is `UseRouting` → `UseAuthentication` → `UseAuthorization`; worth normalising.
7. **Entity models expose `DbSet<T>?` and nullable scalar properties** (`Animal.Name`, `Description`, …) with `[Required]` carrying the validation. This builds cleanly, but making the required columns non-nullable would let EF Core infer `NOT NULL` from the type system instead of relying on the data annotations alone.
