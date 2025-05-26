using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Authorization
{
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permission)
            : base(policy: $"Permission_{permission}")
        {
        }
    }
} 