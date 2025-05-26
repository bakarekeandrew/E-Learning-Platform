using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Dapper;
using E_Learning_Platform.Services;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace E_Learning_Platform.Pages
{
    [Authorize(Policy = "USER.MANAGE")]
    public class UserPermissionsModel : PageModel
    {
        private readonly string _connectionString;
        private readonly IPermissionService _permissionService;
        private readonly ILoggingService _logger;

        public UserPermissionsModel(IConfiguration configuration, IPermissionService permissionService, ILoggingService logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _permissionService = permissionService;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public int UserId { get; set; }

        public string UserFullName { get; set; }
        public List<UserPermission> UserPermissions { get; set; } = new();
        public List<SelectListItem> AvailablePermissions { get; set; } = new();

        [BindProperty]
        public AssignPermissionModel AssignPermissionInput { get; set; } = new();

        public class UserPermission
        {
            public int PermissionId { get; set; }
            public string PermissionName { get; set; }
            public string Description { get; set; }
            public DateTime AssignedDate { get; set; }
            public string AssignedByName { get; set; }
            public string CategoryName { get; set; }
        }

        public class AssignPermissionModel
        {
            public int UserId { get; set; }

            [Required(ErrorMessage = "Permission is required")]
            public int PermissionId { get; set; }

            [Required(ErrorMessage = "Reason is required")]
            public string Reason { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var currentUserId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                return RedirectToPage("/Login");
            }

            // Check if user has permission to manage users
            if (!await _permissionService.HasPermissionAsync(int.Parse(currentUserId), "USER.MANAGE"))
            {
                return RedirectToPage("/AccessDenied");
            }

            UserId = int.Parse(currentUserId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get user details
                var userQuery = "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId";
                UserFullName = await connection.QueryFirstOrDefaultAsync<string>(userQuery, new { UserId = UserId }) ?? "Unknown User";

                // Get user permissions
                var permissionsQuery = @"
                    SELECT 
                        p.PERMISSION_ID as PermissionId,
                        p.PERMISSION_NAME as PermissionName,
                        p.DESCRIPTION as Description,
                        p.CATEGORY_ID as CategoryId,
                        pc.CATEGORY_NAME as CategoryName,
                        up.ASSIGNED_DATE as AssignedDate,
                        u.FULL_NAME as AssignedByName
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN PERMISSION_CATEGORIES pc ON p.CATEGORY_ID = pc.CATEGORY_ID
                    JOIN USERS u ON up.ASSIGNED_BY = u.USER_ID
                    WHERE up.USER_ID = @UserId AND up.IS_GRANT = 1
                    ORDER BY pc.CATEGORY_NAME, p.PERMISSION_NAME";

                UserPermissions = (await connection.QueryAsync<UserPermission>(permissionsQuery, new { UserId = UserId })).ToList();

                // Get available permissions for dropdown
                var availablePermissionsQuery = @"
                    SELECT 
                        p.PERMISSION_ID as Value,
                        p.PERMISSION_NAME + ' - ' + p.DESCRIPTION as Text
                    FROM PERMISSIONS p
                    WHERE NOT EXISTS (
                        SELECT 1 FROM USER_PERMISSIONS up 
                        WHERE up.PERMISSION_ID = p.PERMISSION_ID 
                        AND up.USER_ID = @UserId
                        AND up.IS_GRANT = 1
                    )
                    ORDER BY p.PERMISSION_NAME";

                AvailablePermissions = (await connection.QueryAsync<SelectListItem>(availablePermissionsQuery, new { UserId = UserId })).ToList();

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("UserPermissions", $"Error loading user permissions: {ex.Message}");
                throw;
            }
        }

        public async Task<IActionResult> OnPostAssignPermissionAsync()
        {
            try
            {
                var currentUserId = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return RedirectToPage("/Login");
                }

                // Check if user has permission to manage users
                if (!await _permissionService.HasPermissionAsync(int.Parse(currentUserId), "USER.MANAGE"))
                {
                    return RedirectToPage("/AccessDenied");
                }

                if (!ModelState.IsValid)
                {
                    await OnGetAsync();
                    return Page();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify target user exists
                var userExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM USERS WHERE USER_ID = @UserId",
                    new { UserId = AssignPermissionInput.UserId });

                if (!userExists)
                {
                    ModelState.AddModelError("", "Selected user does not exist.");
                    await OnGetAsync();
                    return Page();
                }

                // Verify permission exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { AssignPermissionInput.PermissionId });

                if (!permissionExists)
                {
                    ModelState.AddModelError("", "Selected permission does not exist.");
                    await OnGetAsync();
                    return Page();
                }

                // Check if permission is already assigned
                var permissionAssigned = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM USER_PERMISSIONS 
                    WHERE USER_ID = @UserId 
                    AND PERMISSION_ID = @PermissionId 
                    AND IS_GRANT = 1",
                    new { 
                        UserId = AssignPermissionInput.UserId,
                        PermissionId = AssignPermissionInput.PermissionId 
                    });

                if (permissionAssigned)
                {
                    ModelState.AddModelError("", "This permission is already assigned to the user.");
                    await OnGetAsync();
                    return Page();
                }

                // Begin transaction
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Add permission
                    var insertQuery = @"
                        INSERT INTO USER_PERMISSIONS (USER_ID, PERMISSION_ID, ASSIGNED_BY, ASSIGNED_DATE, IS_GRANT)
                        VALUES (@UserId, @PermissionId, @AssignedBy, GETDATE(), 1)";

                    await connection.ExecuteAsync(insertQuery, new 
                    { 
                        UserId = AssignPermissionInput.UserId,
                        PermissionId = AssignPermissionInput.PermissionId,
                        AssignedBy = int.Parse(currentUserId)
                    }, transaction);

                    // Log the change
                    var auditQuery = @"
                        INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ASSIGNED_BY, CHANGE_TYPE, CHANGE_REASON, CHANGE_DATE)
                        VALUES (@UserId, @PermissionId, @AssignedBy, 'GRANT', @Reason, GETDATE())";

                    await connection.ExecuteAsync(auditQuery, new
                    {
                        UserId = AssignPermissionInput.UserId,
                        PermissionId = AssignPermissionInput.PermissionId,
                        AssignedBy = int.Parse(currentUserId),
                        Reason = AssignPermissionInput.Reason
                    }, transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("UserPermissions", $"Permission {AssignPermissionInput.PermissionId} assigned to user {AssignPermissionInput.UserId} by user {currentUserId}");

                    TempData["SuccessMessage"] = "Permission assigned successfully.";
                    return RedirectToPage(new { userId = AssignPermissionInput.UserId });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("UserPermissions", $"Error assigning permission: {ex.Message}");
                    ModelState.AddModelError("", "An error occurred while assigning the permission.");
                    await OnGetAsync();
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UserPermissions", $"Error assigning permission: {ex.Message}");
                ModelState.AddModelError("", "An error occurred while processing your request.");
                await OnGetAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostRevokePermissionAsync(int userId, int permissionId)
        {
            var currentUserId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                return RedirectToPage("/Login");
            }

            // Check if user has permission to manage users
            if (!await _permissionService.HasPermissionAsync(int.Parse(currentUserId), "USER.MANAGE"))
            {
                return RedirectToPage("/AccessDenied");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Begin transaction
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Revoke permission
                    var updateQuery = @"
                        UPDATE USER_PERMISSIONS 
                        SET IS_GRANT = 0
                        WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId";

                    await connection.ExecuteAsync(updateQuery, new 
                    { 
                        UserId = userId,
                        PermissionId = permissionId
                    }, transaction);

                    // Log the change
                    var auditQuery = @"
                        INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ASSIGNED_BY, CHANGE_TYPE, CHANGE_DATE)
                        VALUES (@UserId, @PermissionId, @AssignedBy, 'REVOKE', GETDATE())";

                    await connection.ExecuteAsync(auditQuery, new
                    {
                        UserId = userId,
                        PermissionId = permissionId,
                        AssignedBy = int.Parse(currentUserId)
                    }, transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("UserPermissions", $"Permission {permissionId} revoked from user {userId}");

                    return RedirectToPage("/UserPermissions", new { userId });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("UserPermissions", $"Error revoking permission: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UserPermissions", $"Error revoking permission: {ex.Message}");
                throw;
            }
        }
    }
} 