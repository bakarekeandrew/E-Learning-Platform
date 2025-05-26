using Microsoft.AspNetCore.Authorization;
using E_Learning_Platform.Services;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Authorization
{
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public required string Permission { get; init; }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissionService;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(
            IPermissionService permissionService,
            ILogger<PermissionAuthorizationHandler> logger)
        {
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (context == null)
            {
                _logger.LogWarning("Authorization context is null");
                return;
            }

            var userId = context.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("UserId claim not found in the context");
                return;
            }

            try
            {
                var hasPermission = await _permissionService.HasPermissionAsync(
                    int.Parse(userId),
                    requirement.Permission);

                if (hasPermission)
                {
                    context.Succeed(requirement);
                    _logger.LogInformation("Permission {Permission} granted for user {UserId}", requirement.Permission, userId);
                }
                else
                {
                    _logger.LogWarning("Permission {Permission} denied for user {UserId}", requirement.Permission, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking permission {Permission} for user {UserId}", requirement.Permission, userId);
            }
        }
    }
} 