using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Dapper;
using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Data;

namespace E_Learning_Platform.Pages
{
    [Authorize(Roles = "ADMIN")]
    public class PermissionsInfoModel : PageModel
    {
        private readonly string _connectionString;

        public PermissionsInfoModel(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
        }

        // Properties for view
        public List<Permission> Permissions { get; set; } = new();
        public List<Role> Roles { get; set; } = new();
        public Dictionary<int, List<RolePermission>> RolePermissions { get; set; } = new();

        [BindProperty]
        public PermissionInputModel PermissionInput { get; set; } = new();

        [BindProperty]
        public RolePermissionInputModel RolePermissionInput { get; set; } = new();

        // Models
        public class Permission
        {
            public int PermissionId { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
        }

        public class Role
        {
            public int RoleId { get; set; }
            public string RoleName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class RolePermission
        {
            public int RoleId { get; set; }
            public int PermissionId { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public DateTime AssignedDate { get; set; }
            public string AssignedByName { get; set; } = string.Empty;
        }

        public class PermissionInputModel
        {
            public int PermissionId { get; set; }

            [Required(ErrorMessage = "Permission name is required")]
            [StringLength(100, ErrorMessage = "Permission name cannot exceed 100 characters")]
            public string PermissionName { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;
        }

        public class RolePermissionInputModel
        {
            [Required(ErrorMessage = "Role is required")]
            public int RoleId { get; set; }

            [Required(ErrorMessage = "Permission is required")]
            public int PermissionId { get; set; }

            [Required(ErrorMessage = "Reason for assignment is required")]
            public string ChangeReason { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            await LoadPermissionsAsync();
            await LoadRolesAsync();
            await LoadRolePermissionsAsync();
        }

        // Load all permissions
        private async Task LoadPermissionsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    PERMISSION_ID as PermissionId,
                    PERMISSION_NAME as PermissionName,
                    DESCRIPTION as Description,
                    CREATED_DATE as CreatedDate
                FROM PERMISSIONS
                ORDER BY PERMISSION_NAME";

            Permissions = (await connection.QueryAsync<Permission>(query)).ToList();
        }

        // Load all roles
        private async Task LoadRolesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    ROLE_ID as RoleId,
                    ROLE_NAME as RoleName,
                    DESCRIPTION as Description
                FROM ROLES
                ORDER BY ROLE_NAME";

            Roles = (await connection.QueryAsync<Role>(query)).ToList();
        }

        // Load all role permissions
        private async Task LoadRolePermissionsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    rp.ROLE_ID as RoleId,
                    rp.PERMISSION_ID as PermissionId,
                    p.PERMISSION_NAME as PermissionName,
                    rp.ASSIGNED_DATE as AssignedDate,
                    u.FULL_NAME as AssignedByName
                FROM ROLE_PERMISSIONS rp
                JOIN PERMISSIONS p ON rp.PERMISSION_ID = p.PERMISSION_ID
                JOIN USERS u ON rp.ASSIGNED_BY = u.USER_ID
                ORDER BY rp.ROLE_ID, p.PERMISSION_NAME";

            var rolePermissions = await connection.QueryAsync<RolePermission>(query);
            RolePermissions = rolePermissions.GroupBy(rp => rp.RoleId)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        // Add new permission
        public async Task<IActionResult> OnPostAddPermission()
        {
            if (!ModelState.IsValid)
            {
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if permission name already exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_NAME = @PermissionName",
                    new { PermissionInput.PermissionName });

                if (permissionExists)
                {
                    ModelState.AddModelError("PermissionInput.PermissionName", "Permission name already exists");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Insert new permission
                var query = @"
                    INSERT INTO PERMISSIONS (PERMISSION_NAME, DESCRIPTION, CREATED_DATE) 
                    VALUES (@PermissionName, @Description, @CreatedDate)";

                await connection.ExecuteAsync(query, new
                {
                    PermissionInput.PermissionName,
                    PermissionInput.Description,
                    CreatedDate = DateTime.UtcNow
                });

                TempData["SuccessMessage"] = "Permission added successfully!";
                return RedirectToPage();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error occurred: {ex.Message}");
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }
        }

        // Update permission
        public async Task<IActionResult> OnPostUpdatePermission()
        {
            if (!ModelState.IsValid)
            {
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify permission exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { PermissionInput.PermissionId });

                if (!permissionExists)
                {
                    ModelState.AddModelError("", "Permission not found");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Check if permission name already exists for other permissions
                var nameExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_NAME = @PermissionName AND PERMISSION_ID <> @PermissionId",
                    new { PermissionInput.PermissionName, PermissionInput.PermissionId });

                if (nameExists)
                {
                    ModelState.AddModelError("PermissionInput.PermissionName", "Permission name already exists");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Update permission
                var query = @"
                    UPDATE PERMISSIONS SET 
                        PERMISSION_NAME = @PermissionName, 
                        DESCRIPTION = @Description
                    WHERE PERMISSION_ID = @PermissionId";

                await connection.ExecuteAsync(query, new
                {
                    PermissionInput.PermissionName,
                    PermissionInput.Description,
                    PermissionInput.PermissionId
                });

                TempData["SuccessMessage"] = "Permission updated successfully!";
                return RedirectToPage();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error occurred: {ex.Message}");
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }
        }

        // Delete permission
        public async Task<IActionResult> OnPostDeletePermission(int permissionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if permission exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { PermissionId = permissionId });

                if (!permissionExists)
                {
                    return new JsonResult(new { error = "Permission not found" });
                }

                // Check if permission is in use
                var permissionInUse = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM ROLE_PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { PermissionId = permissionId });

                if (permissionInUse)
                {
                    return new JsonResult(new { error = "Cannot delete permission because it is assigned to roles" });
                }

                // Delete permission
                await connection.ExecuteAsync(
                    "DELETE FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { PermissionId = permissionId });

                TempData["SuccessMessage"] = "Permission deleted successfully!";
                return new JsonResult(new { success = true, redirect = Url.Page("PermissionsInfo") });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = $"Error occurred while deleting permission: {ex.Message}" });
            }
        }

        // Assign permission to role
        public async Task<IActionResult> OnPostAssignPermission()
        {
            if (!ModelState.IsValid)
            {
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get current user ID from claims
                var currentUserId = User.FindFirst("USER_ID")?.Value;
                if (string.IsNullOrEmpty(currentUserId) || !int.TryParse(currentUserId, out int userId))
                {
                    ModelState.AddModelError("", "User authentication error");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Check if role exists
                var roleExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM ROLES WHERE ROLE_ID = @RoleId",
                    new { RolePermissionInput.RoleId });

                if (!roleExists)
                {
                    ModelState.AddModelError("RolePermissionInput.RoleId", "Role not found");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Check if permission exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { RolePermissionInput.PermissionId });

                if (!permissionExists)
                {
                    ModelState.AddModelError("RolePermissionInput.PermissionId", "Permission not found");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Check if role already has this permission
                var permissionAssigned = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId",
                    new { RolePermissionInput.RoleId, RolePermissionInput.PermissionId });

                if (permissionAssigned)
                {
                    ModelState.AddModelError("", "This permission is already assigned to the role");
                    await LoadPermissionsAsync();
                    await LoadRolesAsync();
                    await LoadRolePermissionsAsync();
                    return Page();
                }

                // Begin transaction for consistent data
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Insert into ROLE_PERMISSIONS
                    var insertQuery = @"
                        INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, ASSIGNED_DATE, ASSIGNED_BY) 
                        VALUES (@RoleId, @PermissionId, @AssignedDate, @AssignedBy)";

                    await connection.ExecuteAsync(insertQuery, new
                    {
                        RolePermissionInput.RoleId,
                        RolePermissionInput.PermissionId,
                        AssignedDate = DateTime.UtcNow,
                        AssignedBy = userId
                    }, transaction);

                    // Log the change in ROLE_CHANGE_AUDIT
                    var auditQuery = @"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        SELECT u.USER_ID, NULL, @RoleId, @ChangedBy, @ChangeReason, @ChangeDate
                        FROM USERS u
                        WHERE u.ROLE_ID = @RoleId";

                    await connection.ExecuteAsync(auditQuery, new
                    {
                        RoleId = RolePermissionInput.RoleId,
                        ChangedBy = userId,
                        ChangeReason = $"Assigned permission: {RolePermissionInput.ChangeReason}",
                        ChangeDate = DateTime.UtcNow
                    }, transaction);

                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = "Permission assigned to role successfully!";
                    return RedirectToPage();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Transaction failed: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error occurred: {ex.Message}");
                await LoadPermissionsAsync();
                await LoadRolesAsync();
                await LoadRolePermissionsAsync();
                return Page();
            }
        }

        // Remove permission from role
        public async Task<IActionResult> OnPostRemovePermission(int roleId, int permissionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get current user ID from claims
                var currentUserId = User.FindFirst("USER_ID")?.Value;
                if (string.IsNullOrEmpty(currentUserId) || !int.TryParse(currentUserId, out int userId))
                {
                    return new JsonResult(new { error = "User authentication error" });
                }

                // Check if role permission exists
                var permissionAssigned = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId",
                    new { RoleId = roleId, PermissionId = permissionId });

                if (!permissionAssigned)
                {
                    return new JsonResult(new { error = "Permission is not assigned to this role" });
                }

                // Begin transaction for consistent data
                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Get permission name for audit log
                    var permissionName = await connection.ExecuteScalarAsync<string>(
                        "SELECT PERMISSION_NAME FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                        new { PermissionId = permissionId },
                        transaction);

                    // Remove permission from role
                    await connection.ExecuteAsync(
                        "DELETE FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId",
                        new { RoleId = roleId, PermissionId = permissionId },
                        transaction);

                    // Log the change in ROLE_CHANGE_AUDIT
                    var auditQuery = @"
                        INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
                        SELECT u.USER_ID, @RoleId, NULL, @ChangedBy, @ChangeReason, @ChangeDate
                        FROM USERS u
                        WHERE u.ROLE_ID = @RoleId";

                    await connection.ExecuteAsync(auditQuery, new
                    {
                        RoleId = roleId,
                        ChangedBy = userId,
                        ChangeReason = $"Removed permission: {permissionName}",
                        ChangeDate = DateTime.UtcNow
                    }, transaction);

                    await transaction.CommitAsync();

                    TempData["SuccessMessage"] = "Permission removed from role successfully!";
                    return new JsonResult(new { success = true, redirect = Url.Page("PermissionsInfo") });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception($"Transaction failed: {ex.Message}", ex);
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = $"Error occurred while removing permission: {ex.Message}" });
            }
        }

        // Get permission by ID for edit modal
        public async Task<JsonResult> OnGetPermissionById(int permissionId)
        {
            try
            {
                if (permissionId <= 0)
                {
                    return new JsonResult(new { error = "Invalid permission ID" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        PERMISSION_ID as PermissionId,
                        PERMISSION_NAME as PermissionName,
                        DESCRIPTION as Description
                    FROM PERMISSIONS
                    WHERE PERMISSION_ID = @PermissionId";

                var permission = await connection.QueryFirstOrDefaultAsync<PermissionInputModel>(
                    query, new { PermissionId = permissionId });

                if (permission == null)
                {
                    return new JsonResult(new { error = "Permission not found" });
                }

                return new JsonResult(permission);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = $"An error occurred while fetching permission data: {ex.Message}" });
            }
        }
    }
}