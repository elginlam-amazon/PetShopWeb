using System.ComponentModel.DataAnnotations;

namespace PetShopWeb.ViewModels
{
    public class EditUserViewModel
    {
        public string? Id { get; set; }
        [Required]
        public string UserName { get; set; } = string.Empty;
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        public List<string> Claims { get; set; } = new();
        public List<string> Roles { get; set; } = new();
    }
}
