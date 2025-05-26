using Microsoft.AspNetCore.Mvc.RazorPages;
using E_Learning_Platform.Pages.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Shared
{
    public class BasePageModel : PageModel
    {
        protected readonly IPermissionService _permissionService;
        private List<string> _userPermissions;

        public BasePageModel(IPermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        protected async Task<bool> HasPermissionAsync(string permission)
        {
            if (User?.Identity?.IsAuthenticated != true)
                return false;

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            return await _permissionService.HasPermissionAsync(userId, permission);
        }

        protected async Task LoadUserPermissionsAsync()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                _userPermissions = await _permissionService.GetUserPermissionsAsync(userId);
            }
            else
            {
                _userPermissions = new List<string>();
            }
        }

        public bool HasPermission(string permission)
        {
            return _userPermissions?.Contains(permission) ?? false;
        }

        public bool IsAdmin => User?.IsInRole("ADMIN") ?? false;
    }
} 