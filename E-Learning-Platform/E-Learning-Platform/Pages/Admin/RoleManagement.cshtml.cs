using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using E_Learning_Platform.Services;
using System.Security.Claims;
using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using E_Learning_Platform.Models;

namespace E_Learning_Platform.Pages.Admin
{
    [Authorize]
    [RequirePermissions("ROLE.VIEW")]
    public class RoleManagementModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILoggingService _logger;
        private readonly IPermissionService _permissionService;
        private readonly IRoleService _roleService;

        public bool CanManageRoles { get; private set; }

        public RoleManagementModel(
            IConfiguration configuration,
            ILoggingService logger,
            IPermissionService permissionService,
            IRoleService roleService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _permissionService = permissionService;
            _roleService = roleService;
        }

        public List<RoleViewModel> Roles { get; set; } = new();
        public List<SelectListItem> AvailablePermissions { get; set; } = new();
        public List<AuditLogEntry> RecentAuditLogs { get; set; } = new();

        public class RoleViewModel
        {
            public int RoleId { get; set; }
            public string RoleName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int UserCount { get; set; }
            public int PermissionCount { get; set; }
            public bool CanDelete { get; set; }
            public List<PermissionInfo> Permissions { get; set; } = new();
        }

        public class PermissionInfo
        {
            public int PermissionId { get; set; }
            public string PermissionName { get; set; } = string.Empty;
        }

        public class AuditLogEntry
        {
            public DateTime ChangeDate { get; set; }
            public string ChangeDescription { get; set; } = string.Empty;
            public string ChangedByName { get; set; } = string.Empty;
        }

        public class UserRoleViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; }
            public string Email { get; set; }
            public string Username { get; set; }
            public int? RoleId { get; set; }
            public string RoleName { get; set; }
            public bool IsActive { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToPage("/Account/Login");
                }

                CanManageRoles = await _permissionService.HasPermissionAsync(int.Parse(userId), "ROLE.MANAGE");
                if (!CanManageRoles)
                {
                    _logger.LogWarning($"User {userId} attempted to access role management without permission");
                    return RedirectToPage("/AccessDenied");
                }

                await LoadRolesData();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in RoleManagement page: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading role management data.";
                return RedirectToPage("/Error");
            }
        }

        private async Task LoadRolesData()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get roles with user and permission counts
            var rolesQuery = @"
                SELECT 
                    r.ROLE_ID as RoleId,
                    r.ROLE_NAME as RoleName,
                    r.DESCRIPTION as Description,
                    (SELECT COUNT(*) FROM USERS u WHERE u.ROLE_ID = r.ROLE_ID) as UserCount,
                    (SELECT COUNT(*) FROM ROLE_PERMISSIONS rp WHERE rp.ROLE_ID = r.ROLE_ID) as PermissionCount
                FROM ROLES r
                ORDER BY r.ROLE_NAME";

            Roles = (await connection.QueryAsync<RoleViewModel>(rolesQuery)).ToList();

            // Get permissions for each role
            foreach (var role in Roles)
            {
                var permissionsQuery = @"
                    SELECT 
                        p.PERMISSION_ID as PermissionId,
                        p.PERMISSION_NAME as PermissionName
                    FROM ROLE_PERMISSIONS rp
                    JOIN PERMISSIONS p ON rp.PERMISSION_ID = p.PERMISSION_ID
                    WHERE rp.ROLE_ID = @RoleId
                    ORDER BY p.PERMISSION_NAME";

                role.Permissions = (await connection.QueryAsync<PermissionInfo>(
                    permissionsQuery, new { role.RoleId })).ToList();

                // A role can be deleted if it has no users
                role.CanDelete = role.UserCount == 0;
            }

            // Get available permissions for dropdown
            var availablePermissionsQuery = @"
                SELECT 
                    PERMISSION_ID as Value,
                    PERMISSION_NAME as Text
                FROM PERMISSIONS
                ORDER BY PERMISSION_NAME";

            AvailablePermissions = (await connection.QueryAsync<SelectListItem>(
                availablePermissionsQuery)).ToList();

            // Get recent audit logs
            var auditLogsQuery = @"
                SELECT TOP 10
                    rca.CHANGE_DATE as ChangeDate,
                    CASE 
                        WHEN rca.OLD_ROLE_ID IS NULL THEN 'Created role ' + r.ROLE_NAME
                        WHEN rca.NEW_ROLE_ID IS NULL THEN 'Deleted role ' + r.ROLE_NAME
                        ELSE 'Updated role ' + r.ROLE_NAME
                    END as ChangeDescription,
                    ISNULL(u.FULL_NAME, 'System') as ChangedByName
                FROM ROLE_CHANGE_AUDIT rca
                JOIN Roles r ON rca.NEW_ROLE_ID = r.ROLE_ID OR rca.OLD_ROLE_ID = r.ROLE_ID
                LEFT JOIN Users u ON rca.CHANGED_BY = u.USER_ID
                ORDER BY rca.CHANGE_DATE DESC";

            RecentAuditLogs = (await connection.QueryAsync<AuditLogEntry>(auditLogsQuery)).ToList();
        }

        public async Task<JsonResult> OnGetUserRolesAsync(string searchTerm)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return new JsonResult(new { error = "Not authenticated" });
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.VIEW"))
                {
                    return new JsonResult(new { error = "Access denied" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        u.USER_ID as UserId,
                        u.FULL_NAME as FullName,
                        u.EMAIL as Email,
                        u.USERNAME as Username,
                        u.IS_ACTIVE as IsActive,
                        u.ROLE_ID as RoleId,
                        r.ROLE_NAME as RoleName
                    FROM ONLINE_LEARNING_PLATFORM.dbo.USERS u
                    LEFT JOIN ONLINE_LEARNING_PLATFORM.dbo.ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE u.IS_ACTIVE = 1
                    AND (
                        @SearchTerm = '' 
                        OR u.FULL_NAME LIKE @SearchPattern 
                        OR u.EMAIL LIKE @SearchPattern
                        OR u.USERNAME LIKE @SearchPattern
                    )
                    ORDER BY u.FULL_NAME";

                var users = await connection.QueryAsync<UserRoleViewModel>(
                    query,
                    new
                    {
                        SearchTerm = searchTerm ?? "",
                        SearchPattern = $"%{searchTerm}%"
                    });

                return new JsonResult(users);
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error loading user roles: {ex.Message}");
                return new JsonResult(new { error = "Failed to load user roles" });
            }
        }

        public async Task<IActionResult> OnPostCreateRoleAsync(string roleName, string description)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Forbid();
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.MANAGE"))
                {
                    _logger.LogWarning("RoleManagement", $"User {userId} attempted to create role without proper permission");
                    return Forbid();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Check if role name already exists
                    var exists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM Roles WHERE ROLE_NAME = @RoleName",
                        new { RoleName = roleName },
                        transaction);

                    if (exists)
                    {
                        TempData["ErrorMessage"] = "A role with this name already exists.";
                        return RedirectToPage();
                    }

                    // Create role
                    var roleId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO Roles (ROLE_NAME, DESCRIPTION)
                        VALUES (@RoleName, @Description);
                        SELECT SCOPE_IDENTITY();",
                        new { RoleName = roleName, Description = description },
                        transaction);

                    // Log the change
                    await connection.ExecuteAsync(@"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        VALUES (@UserId, @RoleId, @ChangedBy, @Reason, GETDATE())",
                        new { UserId = userId, RoleId = roleId, ChangedBy = userId, Reason = "Role created" },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("RoleManagement", $"User {userId} created new role: {roleName}");
                    TempData["SuccessMessage"] = "Role created successfully.";
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Failed to create role: {ex.Message}");
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error creating role: {ex.Message}");
                TempData["ErrorMessage"] = "Failed to create role.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostUpdateRoleAsync(int roleId, string roleName, string description)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Forbid();
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.MANAGE"))
                {
                    _logger.LogWarning("RoleManagement", $"User {userId} attempted to update role without proper permission");
                    return Forbid();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Get current role details for audit
                    var currentRole = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT ROLE_ID, ROLE_NAME FROM Roles WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction);

                    if (currentRole == null)
                    {
                        TempData["ErrorMessage"] = "Role not found.";
                        return RedirectToPage();
                    }

                    // Check if new name conflicts with existing role
                    var nameConflict = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM Roles WHERE ROLE_NAME = @RoleName AND ROLE_ID <> @RoleId",
                        new { RoleName = roleName, RoleId = roleId },
                        transaction);

                    if (nameConflict)
                    {
                        TempData["ErrorMessage"] = "A role with this name already exists.";
                        return RedirectToPage();
                    }

                    // Update role
                    await connection.ExecuteAsync(@"
                        UPDATE Roles 
                        SET ROLE_NAME = @RoleName, DESCRIPTION = @Description
                        WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId, RoleName = roleName, Description = description },
                        transaction);

                    // Log the change
                    await connection.ExecuteAsync(@"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        VALUES (@UserId, @RoleId, @RoleId, @ChangedBy, @Reason, GETDATE())",
                        new { UserId = userId, RoleId = roleId, ChangedBy = userId, Reason = "Role updated" },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("RoleManagement", $"User {userId} updated role {roleId}");
                    TempData["SuccessMessage"] = "Role updated successfully.";
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Failed to update role: {ex.Message}");
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error updating role: {ex.Message}");
                TempData["ErrorMessage"] = "Failed to update role.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnGetDeleteRoleAsync(int roleId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return Forbid();
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.MANAGE"))
                {
                    _logger.LogWarning("RoleManagement", $"User {userId} attempted to delete role without proper permission");
                    return Forbid();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Check if role has users
                    var hasUsers = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM Users WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction);

                    if (hasUsers)
                    {
                        TempData["ErrorMessage"] = "Cannot delete role: There are users assigned to this role.";
                        return RedirectToPage();
                    }

                    // Get role details for audit before deletion
                    var roleDetails = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        "SELECT ROLE_ID, ROLE_NAME FROM Roles WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction);

                    // Delete role permissions
                    await connection.ExecuteAsync(
                        "DELETE FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction);

                    // Delete role
                    await connection.ExecuteAsync(
                        "DELETE FROM Roles WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction);

                    // Log the change
                    await connection.ExecuteAsync(@"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        VALUES (@UserId, @RoleId, @ChangedBy, @Reason, GETDATE())",
                        new { UserId = userId, RoleId = roleId, ChangedBy = userId, Reason = "Role deleted" },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("RoleManagement", $"User {userId} deleted role {roleId}");
                    TempData["SuccessMessage"] = "Role deleted successfully.";
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Failed to delete role: {ex.Message}");
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error deleting role: {ex.Message}");
                TempData["ErrorMessage"] = "Failed to delete role.";
                return RedirectToPage();
            }
        }

        public async Task<JsonResult> OnPostAddPermissionAsync(int roleId, int permissionId, string reason)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return new JsonResult(new { success = false, error = "Not authenticated" });
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.MANAGE"))
                {
                    return new JsonResult(new { success = false, error = "Access denied" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Check if permission already exists for role
                    var exists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId",
                        new { RoleId = roleId, PermissionId = permissionId },
                        transaction);

                    if (exists)
                    {
                        return new JsonResult(new { success = false, error = "This permission is already assigned to the role." });
                    }

                    // Add permission to role
                    await connection.ExecuteAsync(@"
                        INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID)
                        VALUES (@RoleId, @PermissionId)",
                        new { RoleId = roleId, PermissionId = permissionId },
                        transaction);

                    // Log the change
                    await connection.ExecuteAsync(@"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        VALUES (@UserId, @RoleId, @ChangedBy, @Reason, GETDATE())",
                        new { UserId = userId, RoleId = roleId, ChangedBy = userId, Reason = reason },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("RoleManagement", $"User {userId} added permission {permissionId} to role {roleId}");
                    return new JsonResult(new { success = true });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Failed to add permission: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error adding permission: {ex.Message}");
                return new JsonResult(new { success = false, error = "Failed to add permission." });
            }
        }

        public async Task<JsonResult> OnPostRemovePermissionAsync([FromBody] RemovePermissionModel model)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    return new JsonResult(new { success = false, error = "Not authenticated" });
                }

                if (!await _permissionService.HasPermissionAsync(userId, "ROLE.MANAGE"))
                {
                    return new JsonResult(new { success = false, error = "Access denied" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Remove permission from role
                    var result = await connection.ExecuteAsync(@"
                        DELETE FROM ROLE_PERMISSIONS 
                        WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId",
                        new { model.RoleId, model.PermissionId },
                        transaction);

                    if (result > 0)
                    {
                        // Log the change
                        await connection.ExecuteAsync(@"
                            INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                            VALUES (@UserId, @RoleId, @ChangedBy, @Reason, GETDATE())",
                            new { UserId = userId, RoleId = model.RoleId, ChangedBy = userId, Reason = "Permission removed" },
                            transaction);

                        await transaction.CommitAsync();
                        _logger.LogInfo("RoleManagement", $"User {userId} removed permission {model.PermissionId} from role {model.RoleId}");
                        return new JsonResult(new { success = true });
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return new JsonResult(new { success = false, error = "Permission not found." });
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Failed to remove permission: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error removing permission: {ex.Message}");
                return new JsonResult(new { success = false, error = "Failed to remove permission." });
            }
        }

        public class RemovePermissionModel
        {
            public int RoleId { get; set; }
            public int PermissionId { get; set; }
        }

        public async Task<JsonResult> OnPostChangeUserRoleAsync([FromBody] ChangeUserRoleModel model)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var adminId))
                {
                    return new JsonResult(new { success = false, error = "Not authenticated" });
                }

                if (!await _permissionService.HasPermissionAsync(adminId, "ROLE.MANAGE"))
                {
                    return new JsonResult(new { success = false, error = "Access denied" });
                }

                await _roleService.AssignRoleToUserAsync(model.UserId, model.RoleId, adminId, model.Reason);
                _logger.LogInfo("RoleManagement", $"User {adminId} changed role for user {model.UserId} to role {model.RoleId}");
                    return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError("RoleManagement", $"Error changing user role: {ex.Message}");
                return new JsonResult(new { success = false, error = "Failed to change user role." });
            }
        }

        public class ChangeUserRoleModel
        {
            public int UserId { get; set; }
            public int RoleId { get; set; }
            public string Reason { get; set; }
        }
    }
}