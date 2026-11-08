using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShopWeb.Data;
using PetShopWeb.Repositories.AccountRepository;
using PetShopWeb.Repositories.AnimalRepository;
using PetShopWeb.Repositories.CategoryRepository;
using PetShopWeb.Repositories.CommentRepository;

var builder = WebApplication.CreateBuilder(args);

// Every POST in this app is a server-rendered form (Razor form tag helpers emit the
// antiforgery token automatically), so validation is enforced globally rather than
// relying on a per-action [ValidateAntiForgeryToken].
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddScoped<IAnimalRepository, AnimalRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

string? connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<PetShopContext>(options => options.UseLazyLoadingProxies().UseSqlServer(connectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<PetShopContext>();

// AddIdentity's default cookie paths (/Identity/Account/...) do not exist in this app.
// Point them at the real AccountController actions so the Login/AccessDenied/Logout
// views are reachable on challenge and on authorization failure.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var app = builder.Build();

await SeedDatabaseAsync(app);

// In Development, WebApplicationBuilder already installs the developer exception page,
// which is the intended behaviour here; only non-Development uses the custom error page.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error/ErrorPage");

// HTTPS redirection / HSTS are intentionally omitted: the container image (see Dockerfile)
// terminates TLS at the ingress and serves plain HTTP inside the container, so redirecting
// here would produce a redirect loop.

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}");

app.Run();

// Creates the schema (which also applies the HasData seed rows for Category/Animal/Comment)
// and ensures the "admin" role and the administrator account exist. Idempotent: safe to run
// on every start.
static async Task SeedDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    var ctx = services.GetRequiredService<PetShopContext>();
    // No EF Core migrations exist in this project, so EnsureCreated is the schema strategy.
    // The pre-migration app called EnsureDeleted() before EnsureCreated() on every start, which
    // reset the schema and re-applied the OnModelCreating HasData seed each run. That behaviour is
    // preserved for Development only — dropping a real database on startup in any other
    // environment would destroy data. In non-Development environments EnsureCreated creates the
    // database (and seed) on first run and is a no-op afterwards.
    if (app.Environment.IsDevelopment())
        await ctx.Database.EnsureDeletedAsync();
    await ctx.Database.EnsureCreatedAsync();

    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var logger = services.GetRequiredService<ILogger<Program>>();
    var configuration = services.GetRequiredService<IConfiguration>();

    const string adminRoleName = "admin";
    const string adminEmail = "admin@admin.com";

    if (!await roleManager.RoleExistsAsync(adminRoleName))
    {
        var roleResult = await roleManager.CreateAsync(new IdentityRole(adminRoleName));
        if (!roleResult.Succeeded)
        {
            logger.LogError("Failed to create the '{Role}' role: {Errors}", adminRoleName,
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            return;
        }
    }

    // The probe must match the user that is actually created, otherwise the seed re-runs
    // (and fails) on every start.
    if (await userManager.FindByNameAsync(adminEmail) is not null)
        return;

    // The administrator password is never stored in source. Supply it via
    // Seed:AdminPassword (appsettings.Development.json, user-secrets, or the
    // Seed__AdminPassword environment variable).
    var adminPassword = configuration["Seed:AdminPassword"];
    if (string.IsNullOrEmpty(adminPassword))
    {
        logger.LogWarning(
            "Seed:AdminPassword is not configured — the '{Email}' administrator account was not created. " +
            "Set Seed:AdminPassword (user-secrets or the Seed__AdminPassword environment variable) and restart.",
            adminEmail);
        return;
    }

    var adminUser = new IdentityUser
    {
        UserName = adminEmail,
        Email = adminEmail
    };

    var createResult = await userManager.CreateAsync(adminUser, adminPassword);
    if (!createResult.Succeeded)
    {
        logger.LogError("Failed to create the '{Email}' administrator account: {Errors}", adminEmail,
            string.Join("; ", createResult.Errors.Select(e => e.Description)));
        return;
    }

    var roleAssignment = await userManager.AddToRoleAsync(adminUser, adminRoleName);
    if (!roleAssignment.Succeeded)
    {
        logger.LogError("Failed to add '{Email}' to the '{Role}' role: {Errors}", adminEmail, adminRoleName,
            string.Join("; ", roleAssignment.Errors.Select(e => e.Description)));
    }
}
