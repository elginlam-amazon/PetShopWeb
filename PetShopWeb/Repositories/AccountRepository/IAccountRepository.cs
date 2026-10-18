using Microsoft.AspNetCore.Identity;

namespace PetShopWeb.Repositories.AccountRepository
{
    public interface IAccountRepository
    {
        IQueryable<IdentityRole> GetRoles();
        IQueryable<IdentityUser> GetUsers();
    }
}
