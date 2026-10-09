using System.ComponentModel.DataAnnotations;

namespace PetShopWeb.ViewModels
{
    public class EditUserViewModel
    {
        public string Id { get; set; } = string.Empty;
        [Required]
        public string UserName { get; set; } = string.Empty;
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public List<string> Claims { get; set; }
        public List<string> Roles { get; set; }

        public EditUserViewModel()
        {
            Claims = new List<string>();
            Roles = new List<string>();
        }
    }
}
