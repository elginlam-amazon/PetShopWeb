using System.ComponentModel.DataAnnotations;

namespace PetShopWeb.ViewModels
{
    public class CreateRoleViewModel
    {
        [Required]
        public string RoleName { get; set; } = string.Empty;
    }
}
