using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using E_Learning_Platform.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace E_Learning_Platform.Pages.Admin
{
    [Authorize(Policy = "USER.MANAGE")]
    public class UserRoleManagementModel : PageModel
    {
        private readonly string _connectionString;
        private readonly LoggingService _logger;
        private readonly IRoleService _roleService;
        private readonly IPermissionService _permissionService;

        public UserRoleManagementModel(IConfiguration configuration, LoggingService logger, IRoleService roleService, IPermissionService permissionService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        }

        public class UserViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; }
            public string Email { get; set; }
            public string CurrentRole { get; set; }
        }

        public List<UserViewModel> Users { get; set; } = new();
        public List<SelectListItem> AvailableRoles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var currentUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (currentUserIdClaim == null || !int.TryParse(currentUserIdClaim.Value, out var currentUserId))
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get all users with their current roles
                var usersQuery = @"
                    SELECT 
                        u.USER_ID as UserId,
                        u.FULL_NAME as FullName,
                        u.EMAIL as Email,
                        r.ROLE_NAME as CurrentRole
                    FROM AppUsers u
                    INNER JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    ORDER BY u.FULL_NAME";

                Users = (await connection.QueryAsync<UserViewModel>(usersQuery)).ToList();

                // Get available roles for dropdown
                var rolesQuery = @"
                    SELECT 
                        ROLE_ID as Value,
                        ROLE_NAME as Text
                    FROM ROLES
                    ORDER BY ROLE_NAME";

                AvailableRoles = (await connection.QueryAsync<SelectListItem>(rolesQuery)).ToList();

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("UserRoleManagement", $"Error loading user role management page: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading the users and roles.";
                return RedirectToPage("/AdminDashboard");
            }
        }

        public async Task<IActionResult> OnPostChangeUserRoleAsync(int userId, int newRoleId, string reason)
        {
            try
            {
                var currentUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (currentUserIdClaim == null || !int.TryParse(currentUserIdClaim.Value, out var currentUserId))
                {
                    return RedirectToPage("/Login");
                }

                // Check if user has permission to manage roles
                if (!await _permissionService.HasPermissionAsync(currentUserId, "ROLE.MANAGE"))
                {
                    TempData["ErrorMessage"] = "You don't have permission to manage roles.";
                    return RedirectToPage();
                }

                try
                {
                    await _roleService.AssignRoleToUserAsync(userId, newRoleId, currentUserId, reason);
                    TempData["SuccessMessage"] = "User role updated successfully.";
                }
                catch (Exception ex)
                {
                    _logger.LogError("UserRoleManagement", $"Error changing user role: {ex.Message}");
                    TempData["ErrorMessage"] = "An error occurred while updating the user's role.";
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("UserRoleManagement", $"Error in role change process: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while processing your request.";
                return RedirectToPage();
            }
        }
    }
} 