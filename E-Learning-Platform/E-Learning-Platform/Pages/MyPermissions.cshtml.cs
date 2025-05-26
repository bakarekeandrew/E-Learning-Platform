using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;
using E_Learning_Platform.Services;
using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Pages
{
    [Authorize]
    public class MyPermissionsModel : PageModel
    {
        private readonly string _connectionString;
        private readonly IPermissionService _permissionService;
        private readonly ILoggingService _logger;

        public List<UserPermissionViewModel> UserPermissions { get; set; } = new();
        public List<PermissionHistoryEntry> PermissionHistory { get; set; } = new();

        public MyPermissionsModel(IConfiguration configuration, IPermissionService permissionService, ILoggingService logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("DefaultConnection string not found in configuration.");
            _permissionService = permissionService;
            _logger = logger;
        }

        public class UserPermissionViewModel
        {
            public string PermissionName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public bool IsDirectPermission { get; set; }
            public DateTime AssignedDate { get; set; }
            public string AssignedByName { get; set; } = string.Empty;
        }

        public class PermissionHistoryEntry
        {
            public DateTime ChangeDate { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public string ChangeType { get; set; } = string.Empty;
            public string ChangedByName { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToPage("/Login");
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get direct permissions
                var directPermissionsQuery = @"
                    SELECT 
                        p.PERMISSION_NAME as PermissionName,
                        p.DESCRIPTION as Description,
                        ISNULL(pc.CATEGORY_NAME, 'General') as CategoryName,
                        1 as IsDirectPermission,
                        up.ASSIGNED_DATE as AssignedDate,
                        ISNULL(u.FULL_NAME, 'System') as AssignedByName
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN PERMISSION_CATEGORIES pc ON p.CATEGORY_ID = pc.CATEGORY_ID
                    LEFT JOIN USERS u ON up.ASSIGNED_BY = u.USER_ID
                    WHERE up.USER_ID = @UserId 
                    AND up.IS_GRANT = 1
                    AND (up.EXPIRATION_DATE IS NULL OR up.EXPIRATION_DATE > GETDATE())";

                var directPermissions = await connection.QueryAsync<UserPermissionViewModel>(
                    directPermissionsQuery, new { UserId = int.Parse(userId) });

                // Get role-based permissions
                var rolePermissionsQuery = @"
                    SELECT DISTINCT
                        p.PERMISSION_NAME as PermissionName,
                        p.DESCRIPTION as Description,
                        ISNULL(pc.CATEGORY_NAME, 'General') as CategoryName,
                        0 as IsDirectPermission,
                        rp.ASSIGNED_DATE as AssignedDate,
                        ISNULL(u.FULL_NAME, 'System') as AssignedByName
                    FROM USERS usr
                    JOIN USER_ROLES ur ON usr.USER_ID = ur.USER_ID
                    JOIN ROLE_PERMISSIONS rp ON ur.ROLE_ID = rp.ROLE_ID
                    JOIN PERMISSIONS p ON rp.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN PERMISSION_CATEGORIES pc ON p.CATEGORY_ID = pc.CATEGORY_ID
                    LEFT JOIN USERS u ON rp.ASSIGNED_BY = u.USER_ID
                    WHERE usr.USER_ID = @UserId";

                var rolePermissions = await connection.QueryAsync<UserPermissionViewModel>(
                    rolePermissionsQuery, new { UserId = int.Parse(userId) });

                UserPermissions = directPermissions.Union(rolePermissions)
                    .OrderBy(p => p.CategoryName)
                    .ThenBy(p => p.PermissionName)
                    .ToList();

                // Get permission history
                var historyQuery = @"
                    SELECT 
                        up.ASSIGNED_DATE as ChangeDate,
                        p.PERMISSION_NAME as PermissionName,
                        CASE WHEN up.IS_GRANT = 1 THEN 'GRANT' ELSE 'REVOKE' END as ChangeType,
                        ISNULL(u.FULL_NAME, 'System') as ChangedByName,
                        '' as Reason
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN USERS u ON up.ASSIGNED_BY = u.USER_ID
                    WHERE up.USER_ID = @UserId
                    ORDER BY up.ASSIGNED_DATE DESC";

                PermissionHistory = (await connection.QueryAsync<PermissionHistoryEntry>(
                    historyQuery, new { UserId = int.Parse(userId) })).ToList();

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("MyPermissions", $"Error loading permissions: {ex.Message}");
                throw;
            }
        }
    }
} 