using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionsAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private readonly string[] _permissions;

        public RequirePermissionsAttribute(params string[] permissions)
        {
            _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var permissionService = context.HttpContext.RequestServices.GetService(typeof(IPermissionService)) as IPermissionService;
            if (permissionService == null)
            {
                context.Result = new StatusCodeResult(500);
                return;
            }

            var userIdClaim = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            foreach (var permission in _permissions)
            {
                if (!await permissionService.HasPermissionAsync(userId, permission))
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }
        }
    }
} 