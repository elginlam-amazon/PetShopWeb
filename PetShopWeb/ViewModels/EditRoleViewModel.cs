using System.ComponentModel.DataAnnotations;

namespace PetShopWeb.ViewModels
{
    public class EditRoleViewModel
    {
        public string Id { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role Name is required")]
        [Display(Name = "Role name")]
        public string RoleName { get; set; } = string.Empty;
        public List<string> Users { get; set; }

        public EditRoleViewModel()
        {
            Users = new();
        }
    }
}
