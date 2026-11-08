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

string connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
builder.Services.AddDbContext<PetShopContext>(options => options.UseLazyLoadingProxies().UseSqlServer(connectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<PetShopContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<PetShopContext>();
    ctx.Database.EnsureDeleted();
    ctx.Database.EnsureCreated();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var adminRole = new IdentityRole("admin");
    if (!ctx.Roles.Any())
        roleManager.CreateAsync(adminRole).GetAwaiter().GetResult();

    var seedPassword = builder.Configuration["SeedAdmin:Password"];
    if (!ctx.Users.Any(u => u.UserName == "Admin"))
    {
        if (string.IsNullOrEmpty(seedPassword))
        {
            app.Logger.LogWarning("SeedAdmin:Password is not configured; the development admin user was not created.");
        }
        else
        {
            var adminUser = new IdentityUser()
            {
                UserName = "admin@admin.com",
                Email = "admin@admin.com"
            };
            var result = userManager.CreateAsync(adminUser, seedPassword).GetAwaiter().GetResult();
            if (result.Succeeded)
            {
                userManager.AddToRoleAsync(adminUser, adminRole.Name!).GetAwaiter().GetResult();
            }
            else
            {
                app.Logger.LogWarning("The development admin user was not created: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error/ErrorPage");

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=HomePage}/{id?}");

app.Run();
