using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PetShopWeb.Data;
using PetShopWeb.Repositories.AccountRepository;
using PetShopWeb.Repositories.AnimalRepository;
using PetShopWeb.Repositories.CategoryRepository;
using PetShopWeb.Repositories.CommentRepository;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IAnimalRepository, AnimalRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<PetShopContext>(options => options.UseLazyLoadingProxies().UseSqlServer(connectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<PetShopContext>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<PetShopContext>();

    // In Development, wipe the database so seed data is always fresh.
    if (app.Environment.IsDevelopment())
        await ctx.Database.EnsureDeletedAsync();

    // Apply pending migrations (creates schema + HasData seed rows).
    await ctx.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    if (!await ctx.Roles.AnyAsync())
    {
        var adminRole = new IdentityRole("admin");
        await roleManager.CreateAsync(adminRole);
    }

    if (!await ctx.Users.AnyAsync(u => u.UserName == "admin@admin.com"))
    {
        string adminPassword = builder.Configuration["Seed:AdminPassword"]
            ?? throw new InvalidOperationException(
                "Seed:AdminPassword is not configured. " +
                "Set it via appsettings, user-secrets, or the Seed__AdminPassword environment variable.");

        var adminUser = new IdentityUser
        {
            UserName = "admin@admin.com",
            Email = "admin@admin.com"
        };
        await userManager.CreateAsync(adminUser, adminPassword);
        await userManager.AddToRoleAsync(adminUser, "admin");
    }
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error/ErrorPage");

app.UseStaticFiles();

app.UseAuthentication();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}");
app.Run();
