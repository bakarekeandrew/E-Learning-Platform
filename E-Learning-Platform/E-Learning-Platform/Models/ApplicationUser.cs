using Microsoft.AspNetCore.Identity;

namespace E_Learning_Platform.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Role { get; set; }
    }
} 