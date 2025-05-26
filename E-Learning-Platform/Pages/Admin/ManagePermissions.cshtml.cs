using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using E_Learning_Platform.Models;
using E_Learning_Platform.Pages.Services;
using E_Learning_Platform.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace E_Learning_Platform.Pages.Admin
{
    public class ManagePermissionsModel : BasePageModel
    {
        private readonly string _connectionString;

        public List<UserInfo> Users { get; set; }
        public List<PermissionCategory> PermissionCategories { get; set; }
        public List<Permission> Permissions { get; set; }
        public List<PermissionAssignment> PermissionAssignments { get; set; }

        public ManagePermissionsModel(IPermissionService permissionService, IConfiguration configuration)
            : base(permissionService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await HasPermissionAsync("USER.MANAGE"))
            {
                return Forbid();
            }

            await LoadData();
            return Page();
        }

        public async Task<IActionResult> OnPostAssignPermissionAsync(int userId, int permissionId, DateTime? expirationDate)
        {
            if (!await HasPermissionAsync("USER.MANAGE"))
            {
                return Forbid();
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if permission already exists
            var existingPermission = await connection.QueryFirstOrDefaultAsync<UserPermissionInfo>(
                "SELECT * FROM USER_PERMISSIONS WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId",
                new { UserId = userId, PermissionId = permissionId });

            if (existingPermission != null)
            {
                // Update existing permission
                await connection.ExecuteAsync(
                    @"UPDATE USER_PERMISSIONS 
                    SET IS_GRANT = 1, 
                        EXPIRATION_DATE = @ExpirationDate,
                        ASSIGNED_BY = @AssignedBy,
                        ASSIGNED_DATE = GETDATE()
                    WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId",
                    new { 
                        UserId = userId, 
                        PermissionId = permissionId, 
                        ExpirationDate = expirationDate,
                        AssignedBy = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value)
                    });
            }
            else
            {
                // Insert new permission
                await connection.ExecuteAsync(
                    @"INSERT INTO USER_PERMISSIONS (USER_ID, PERMISSION_ID, IS_GRANT, ASSIGNED_BY, ASSIGNED_DATE, EXPIRATION_DATE)
                    VALUES (@UserId, @PermissionId, 1, @AssignedBy, GETDATE(), @ExpirationDate)",
                    new { 
                        UserId = userId, 
                        PermissionId = permissionId, 
                        ExpirationDate = expirationDate,
                        AssignedBy = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value)
                    });
            }

            // Log the permission assignment
            await connection.ExecuteAsync(
                @"INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ACTION, CHANGED_BY, CHANGE_DATE, REASON)
                VALUES (@UserId, @PermissionId, 'ASSIGN', @ChangedBy, GETDATE(), @Reason)",
                new { 
                    UserId = userId, 
                    PermissionId = permissionId,
                    ChangedBy = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value),
                    Reason = "Permission assigned through admin interface"
                });

            await LoadData();
            return Page();
        }

        public async Task<IActionResult> OnPostRevokePermissionAsync(int userId, int permissionId)
        {
            if (!await HasPermissionAsync("USER.MANAGE"))
            {
                return Forbid();
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Revoke the permission
            await connection.ExecuteAsync(
                "UPDATE USER_PERMISSIONS SET IS_GRANT = 0 WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId",
                new { UserId = userId, PermissionId = permissionId });

            // Log the revocation
            await connection.ExecuteAsync(
                @"INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ACTION, CHANGED_BY, CHANGE_DATE, REASON)
                VALUES (@UserId, @PermissionId, 'REVOKE', @ChangedBy, GETDATE(), @Reason)",
                new { 
                    UserId = userId, 
                    PermissionId = permissionId,
                    ChangedBy = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value),
                    Reason = "Permission revoked through admin interface"
                });

            await LoadData();
            return Page();
        }

        private async Task LoadData()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Load users
            Users = (await connection.QueryAsync<UserInfo>(
                "SELECT USER_ID, USERNAME, FULL_NAME FROM USERS WHERE IS_ACTIVE = 1")).AsList();

            // Load permission categories
            PermissionCategories = (await connection.QueryAsync<PermissionCategory>(
                "SELECT * FROM PERMISSION_CATEGORIES")).AsList();

            // Load permissions
            Permissions = (await connection.QueryAsync<Permission>(
                "SELECT * FROM PERMISSIONS")).AsList();

            // Load current permission assignments
            PermissionAssignments = (await connection.QueryAsync<PermissionAssignment>(@"
                SELECT 
                    UP.USER_ID as UserId,
                    U.FULL_NAME as UserName,
                    UP.PERMISSION_ID as PermissionId,
                    P.PERMISSION_NAME as PermissionName,
                    PC.CATEGORY_NAME as CategoryName,
                    UP.ASSIGNED_DATE as AssignedDate,
                    UP.EXPIRATION_DATE as ExpirationDate
                FROM USER_PERMISSIONS UP
                JOIN USERS U ON UP.USER_ID = U.USER_ID
                JOIN PERMISSIONS P ON UP.PERMISSION_ID = P.PERMISSION_ID
                JOIN PERMISSION_CATEGORIES PC ON P.CATEGORY_ID = PC.CATEGORY_ID
                WHERE UP.IS_GRANT = 1
                ORDER BY U.FULL_NAME, P.PERMISSION_NAME")).AsList();
        }
    }

    public class PermissionCategory
    {
        public int CATEGORY_ID { get; set; }
        public string CATEGORY_NAME { get; set; }
        public string DESCRIPTION { get; set; }
    }

    public class Permission
    {
        public int PERMISSION_ID { get; set; }
        public string PERMISSION_NAME { get; set; }
        public string DESCRIPTION { get; set; }
        public int CATEGORY_ID { get; set; }
    }

    public class PermissionAssignment
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public int PermissionId { get; set; }
        public string PermissionName { get; set; }
        public string CategoryName { get; set; }
        public DateTime AssignedDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
    }
} 