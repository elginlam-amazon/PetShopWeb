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
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' has not been configured.");
builder.Services.AddDbContext<PetShopContext>(options => options.UseLazyLoadingProxies().UseSqlServer(connectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<PetShopContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<PetShopContext>();
    ctx.Database.EnsureDeleted();
    ctx.Database.EnsureCreated();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    const string adminRoleName = "admin";

    if(!ctx.Roles.Any())
        roleManager.CreateAsync(new IdentityRole(adminRoleName)).GetAwaiter().GetResult();

    if(!ctx.Users.Any(u => u.UserName == "Admin"))
    {
        var adminUser = new IdentityUser()
        {
            UserName = "admin@admin.com",
            Email = "admin@admin.com"
        };
        var result = userManager.CreateAsync(adminUser, "Admin123").GetAwaiter().GetResult();
        if (result.Succeeded)
            userManager.AddToRoleAsync(adminUser, adminRoleName).GetAwaiter().GetResult();
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
