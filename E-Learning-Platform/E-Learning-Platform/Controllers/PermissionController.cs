using Microsoft.AspNetCore.Mvc;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionService _permissionService;
        private readonly ILoggingService _logger;

        public PermissionController(IPermissionService permissionService, ILoggingService logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetUserPermissions(int userId)
        {
            try
            {
                var permissions = await _permissionService.GetUserPermissionsAsync(userId);
                return Ok(new { UserId = userId, Permissions = permissions });
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionController", $"Error getting permissions for user {userId}: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("check/{userId}/{permissionName}")]
        public async Task<IActionResult> CheckPermission(int userId, string permissionName)
        {
            try
            {
                var hasPermission = await _permissionService.HasPermissionAsync(userId, permissionName);
                return Ok(new { UserId = userId, Permission = permissionName, HasPermission = hasPermission });
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionController", $"Error checking permission {permissionName} for user {userId}: {ex.Message}");
                return StatusCode(500, "Internal server error");
            }
        }
    }
} 