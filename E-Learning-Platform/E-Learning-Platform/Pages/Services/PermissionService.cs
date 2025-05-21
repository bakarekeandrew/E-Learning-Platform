using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Data;
using Dapper;

namespace E_Learning_Platform.Pages.Services
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string permission);
        Task<List<string>> GetUserPermissionsAsync(int userId);
        Task GrantPermissionAsync(int userId, int permissionId, int assignedBy, string reason);
        Task RevokePermissionAsync(int userId, int permissionId, int revokedBy, string reason);
        Task AssignRolePermissionAsync(int roleId, int permissionId, int assignedBy, string reason);
        Task RemoveRolePermissionAsync(int roleId, int permissionId, int removedBy, string reason);
        Task<bool> UserExistsAsync(int userId);
        Task<bool> RoleExistsAsync(int roleId);
        Task<bool> PermissionExistsAsync(int permissionId);
        Task<bool> RoleHasPermissionAsync(int roleId, int permissionId);
        Task<IDbTransaction> BeginTransactionAsync();
        Task<UserPermission> GetUserPermissionAsync(int userId, int permissionId);
        Task UpdateUserPermissionAsync(int userId, int permissionId, bool isGrant, string assignedBy, string reason, IDbTransaction transaction);
        Task LogPermissionChangeAsync(int userId, int permissionId, string action, string changedBy, string reason, IDbTransaction transaction);
    }

    public class UserPermissionInput
    {
        public int UserId { get; set; }
        public int PermissionId { get; set; }
        public bool IsGrant { get; set; }
        public string ChangeReason { get; set; } = string.Empty;
    }

    public class UserPermission
    {
        public int UserId { get; set; }
        public int PermissionId { get; set; }
        public string PermissionName { get; set; } = string.Empty;
        public DateTime AssignedDate { get; set; }
        public string AssignedByName { get; set; } = string.Empty;
        public bool IsGrant { get; set; }
    }

    public class PermissionService : IPermissionService
    {
        private readonly string _connectionString;

        public PermissionService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public async Task<bool> HasPermissionAsync(int userId, string permission)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT COUNT(1)
                FROM USERS U
                JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID
                WHERE U.USER_ID = @UserId
                AND R.ROLE_NAME = 'ADMIN'";

            var hasPermission = await connection.ExecuteScalarAsync<int>(query, new { UserId = userId });
            return hasPermission > 0;
        }

        public async Task<List<string>> GetUserPermissionsAsync(int userId)
        {
            var permissions = new List<string>();
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Get direct user permissions
                var userPermQuery = @"
                    SELECT p.PERMISSION_NAME
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    WHERE up.USER_ID = @UserId AND up.IS_GRANT = 1
                    AND (up.EXPIRATION_DATE IS NULL OR up.EXPIRATION_DATE > GETDATE())";

                using (var userPermCmd = new SqlCommand(userPermQuery, connection))
                {
                    userPermCmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = await userPermCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            permissions.Add(reader.GetString(0));
                        }
                    }
                }

                // Get role-based permissions
                var rolePermQuery = @"
                    SELECT p.PERMISSION_NAME
                    FROM USERS u
                    JOIN ROLE_PERMISSIONS rp ON u.ROLE_ID = rp.ROLE_ID
                    JOIN PERMISSIONS p ON rp.PERMISSION_ID = p.PERMISSION_ID
                    WHERE u.USER_ID = @UserId";

                using (var rolePermCmd = new SqlCommand(rolePermQuery, connection))
                {
                    rolePermCmd.Parameters.AddWithValue("@UserId", userId);
                    using (var reader = await rolePermCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            permissions.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return permissions.Distinct().ToList();
        }

        public async Task GrantPermissionAsync(int userId, int permissionId, int assignedBy, string reason)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = @"
                    INSERT INTO USER_PERMISSIONS (USER_ID, PERMISSION_ID, ASSIGNED_BY, IS_GRANT, ASSIGNED_DATE)
                    VALUES (@UserId, @PermissionId, @AssignedBy, 1, GETDATE())";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    cmd.Parameters.AddWithValue("@AssignedBy", assignedBy);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task RevokePermissionAsync(int userId, int permissionId, int revokedBy, string reason)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = @"
                    UPDATE USER_PERMISSIONS 
                    SET IS_GRANT = 0, 
                        ASSIGNED_BY = @RevokedBy, 
                        ASSIGNED_DATE = GETDATE()
                    WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    cmd.Parameters.AddWithValue("@RevokedBy", revokedBy);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task AssignRolePermissionAsync(int roleId, int permissionId, int assignedBy, string reason)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = @"
                    INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, ASSIGNED_BY, ASSIGNED_DATE, REASON)
                    VALUES (@RoleId, @PermissionId, @AssignedBy, GETDATE(), @Reason)";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@RoleId", roleId);
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    cmd.Parameters.AddWithValue("@AssignedBy", assignedBy);
                    cmd.Parameters.AddWithValue("@Reason", reason);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task RemoveRolePermissionAsync(int roleId, int permissionId, int removedBy, string reason)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = @"
                    DELETE FROM ROLE_PERMISSIONS 
                    WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@RoleId", roleId);
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<bool> UserExistsAsync(int userId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = "SELECT COUNT(1) FROM USERS WHERE USER_ID = @UserId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
            }
        }

        public async Task<bool> RoleExistsAsync(int roleId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = "SELECT COUNT(1) FROM ROLES WHERE ROLE_ID = @RoleId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@RoleId", roleId);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
            }
        }

        public async Task<bool> PermissionExistsAsync(int permissionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
            }
        }

        public async Task<bool> RoleHasPermissionAsync(int roleId, int permissionId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                var query = "SELECT COUNT(1) FROM ROLE_PERMISSIONS WHERE ROLE_ID = @RoleId AND PERMISSION_ID = @PermissionId";
                using (var cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@RoleId", roleId);
                    cmd.Parameters.AddWithValue("@PermissionId", permissionId);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
            }
        }

        public async Task<IDbTransaction> BeginTransactionAsync()
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            return await connection.BeginTransactionAsync();
        }

        public async Task<UserPermission> GetUserPermissionAsync(int userId, int permissionId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    up.USER_ID as UserId,
                    up.PERMISSION_ID as PermissionId,
                    p.PERMISSION_NAME as PermissionName,
                    up.ASSIGNED_DATE as AssignedDate,
                    u.FULL_NAME as AssignedByName,
                    up.IS_GRANT as IsGrant
                FROM USER_PERMISSIONS up
                JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                JOIN USERS u ON up.ASSIGNED_BY = u.USER_ID
                WHERE up.USER_ID = @UserId AND up.PERMISSION_ID = @PermissionId";

            return await connection.QueryFirstOrDefaultAsync<UserPermission>(query, new { UserId = userId, PermissionId = permissionId });
        }

        public async Task UpdateUserPermissionAsync(int userId, int permissionId, bool isGrant, string assignedBy, string reason, IDbTransaction transaction)
        {
            var query = @"
                UPDATE USER_PERMISSIONS 
                SET IS_GRANT = @IsGrant,
                    ASSIGNED_BY = @AssignedBy,
                    ASSIGNED_DATE = GETDATE()
                WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId";

            await ((SqlConnection)transaction.Connection).ExecuteAsync(query, new
            {
                UserId = userId,
                PermissionId = permissionId,
                IsGrant = isGrant,
                AssignedBy = assignedBy
            }, transaction);
        }

        public async Task LogPermissionChangeAsync(int userId, int permissionId, string action, string changedBy, string reason, IDbTransaction transaction)
        {
            var query = @"
                INSERT INTO PERMISSION_AUDIT_LOG 
                    (USER_ID, PERMISSION_ID, ACTION, CHANGED_BY, CHANGE_DATE, REASON)
                VALUES 
                    (@UserId, @PermissionId, @Action, @ChangedBy, GETDATE(), @Reason)";

            await ((SqlConnection)transaction.Connection).ExecuteAsync(query, new
            {
                UserId = userId,
                PermissionId = permissionId,
                Action = action,
                ChangedBy = changedBy,
                Reason = reason
            }, transaction);
        }
    }
}