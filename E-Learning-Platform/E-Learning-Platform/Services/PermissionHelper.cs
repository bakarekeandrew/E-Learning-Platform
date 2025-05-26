using Microsoft.AspNetCore.Mvc.RazorPages;

namespace E_Learning_Platform.Services
{
    public static class PermissionHelper
    {
        public static async Task<bool> HasPermissionAsync(this PageModel page, string permissionName)
        {
            var userId = page.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
                return false;

            var permissionService = page.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
            return await permissionService.HasPermissionAsync(int.Parse(userId), permissionName);
        }
    }
} 