using Microsoft.Data.SqlClient;
using Dapper;
using System.Security.Claims;

namespace E_Learning_Platform.Services
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string permissionName);
        Task<IEnumerable<string>> GetUserPermissionsAsync(int userId);
        Task<bool> GrantPermissionAsync(int userId, string permissionName);
        Task<bool> RevokePermissionAsync(int userId, string permissionName);
    }

    public class PermissionService : IPermissionService
    {
        private readonly string _connectionString;
        private readonly ILoggingService _logger;
        private readonly INotificationService _notificationService;

        public PermissionService(
            IConfiguration configuration, 
            ILoggingService logger,
            INotificationService notificationService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task<bool> HasPermissionAsync(int userId, string permissionName)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                _logger.LogInfo("PermissionService", $"Checking permission {permissionName} for user {userId}");

                // First check if user is active
                var isActive = await connection.ExecuteScalarAsync<bool>(
                    "SELECT IS_ACTIVE FROM Users WHERE USER_ID = @UserId",
                    new { UserId = userId });

                if (!isActive)
                {
                    _logger.LogInfo("PermissionService", $"User {userId} is not active");
                    return false;
                }

                var sql = @"
                    -- Check direct user permissions
                    SELECT COUNT(1) as PermCount
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    WHERE up.USER_ID = @UserId 
                    AND p.PERMISSION_NAME = @PermissionName
                    AND up.IS_GRANT = 1
                    AND (up.EXPIRATION_DATE IS NULL OR up.EXPIRATION_DATE > GETDATE());

                    -- Check role-based permissions
                    SELECT COUNT(1) as PermCount
                    FROM Users u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    JOIN ROLE_PERMISSIONS rp ON r.ROLE_ID = rp.ROLE_ID
                    JOIN PERMISSIONS p ON rp.PERMISSION_ID = p.PERMISSION_ID
                    WHERE u.USER_ID = @UserId 
                    AND p.PERMISSION_NAME = @PermissionName;

                    -- Get all user permissions for logging
                    SELECT p.PERMISSION_NAME
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    WHERE up.USER_ID = @UserId 
                    AND up.IS_GRANT = 1
                    AND (up.EXPIRATION_DATE IS NULL OR up.EXPIRATION_DATE > GETDATE());

                    -- Get user's role name for logging
                    SELECT r.ROLE_NAME
                    FROM Users u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE u.USER_ID = @UserId;";

                using var multi = await connection.QueryMultipleAsync(sql, new { UserId = userId, PermissionName = permissionName });
                
                var directPermCount = await multi.ReadFirstAsync<int>();
                var rolePermCount = await multi.ReadFirstAsync<int>();
                var userPerms = await multi.ReadAsync<string>();
                var roleName = await multi.ReadFirstOrDefaultAsync<string>();

                _logger.LogInfo("PermissionService", $"User {userId} has role: {roleName}");
                _logger.LogInfo("PermissionService", $"User {userId} direct permissions: {string.Join(", ", userPerms)}");
                _logger.LogInfo("PermissionService", $"Permission counts - Direct: {directPermCount}, Role: {rolePermCount}");

                // Special handling for ADMIN.ACCESS
                if (userPerms.Any(p => p == "ADMIN.ACCESS"))
                {
                    _logger.LogInfo("PermissionService", $"User {userId} has ADMIN.ACCESS - granting {permissionName}");
                    return true;
                }

                // Check for parent permissions
                if (permissionName.StartsWith("USER.") && userPerms.Any(p => p == "USER.MANAGE"))
                {
                    _logger.LogInfo("PermissionService", $"User {userId} has USER.MANAGE - granting {permissionName}");
                    return true;
                }

                if (permissionName.StartsWith("COURSE.") && userPerms.Any(p => p == "COURSE.MANAGE"))
                {
                    _logger.LogInfo("PermissionService", $"User {userId} has COURSE.MANAGE - granting {permissionName}");
                    return true;
                }

                var hasPermission = directPermCount > 0 || rolePermCount > 0;
                _logger.LogInfo("PermissionService", $"Final permission check for user {userId}: {permissionName} = {hasPermission}");
                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionService", $"Error checking permission: {ex.Message}");
                return false;
            }
        }

        public async Task<IEnumerable<string>> GetUserPermissionsAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT DISTINCT p.PERMISSION_NAME
                    FROM PERMISSIONS p
                    LEFT JOIN USER_PERMISSIONS up ON p.PERMISSION_ID = up.PERMISSION_ID
                    LEFT JOIN ROLE_PERMISSIONS rp ON p.PERMISSION_ID = rp.PERMISSION_ID
                    LEFT JOIN AppUsers u ON (u.USER_ID = up.USER_ID OR u.ROLE_ID = rp.ROLE_ID)
                    WHERE u.USER_ID = @UserId
                    AND (up.IS_GRANT = 1 OR rp.IS_GRANT = 1)
                    ORDER BY p.PERMISSION_NAME";

                var permissions = (await connection.QueryAsync<string>(sql, new { UserId = userId })).AsList();
                _logger.LogInfo("PermissionService", $"Retrieved {permissions.Count} permissions for user {userId}");
                return permissions;
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionService", $"Error getting user permissions: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        public async Task<bool> GrantPermissionAsync(int userId, string permissionName)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Get user's name
                    var userName = await connection.QuerySingleOrDefaultAsync<string>(
                        "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId },
                        transaction);

                    // Get the permission ID and details
                    var permissionDetails = await connection.QuerySingleOrDefaultAsync<(int Id, string Description)>(
                        "SELECT PERMISSION_ID as Id, DESCRIPTION as Description FROM PERMISSIONS WHERE PERMISSION_NAME = @PermissionName",
                        new { PermissionName = permissionName },
                        transaction);

                    if (permissionDetails.Id == 0)
                    {
                        _logger.LogWarning("PermissionService", $"Permission {permissionName} not found");
                        return false;
                    }

                    // Grant the permission using MERGE statement
                    await connection.ExecuteAsync(@"
                        MERGE USER_PERMISSIONS AS target
                        USING (SELECT @UserId as USER_ID, @PermissionId as PERMISSION_ID) AS source
                        ON target.USER_ID = source.USER_ID AND target.PERMISSION_ID = source.PERMISSION_ID
                        WHEN MATCHED THEN
                            UPDATE SET 
                                IS_GRANT = 1,
                                ASSIGNED_DATE = GETDATE()
                        WHEN NOT MATCHED THEN
                            INSERT (USER_ID, PERMISSION_ID, IS_GRANT, ASSIGNED_DATE)
                            VALUES (@UserId, @PermissionId, 1, GETDATE());",
                        new { UserId = userId, PermissionId = permissionDetails.Id },
                        transaction);

                    // Log the change in audit
                    await connection.ExecuteAsync(@"
                        INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ACTION, CHANGE_DATE, REASON)
                        VALUES (@UserId, @PermissionId, 'GRANT', GETDATE(), @Reason)",
                        new { 
                            UserId = userId, 
                            PermissionId = permissionDetails.Id,
                            Reason = $"Permission granted: {permissionName}"
                        },
                        transaction);

                    // Get admin user IDs
                    var adminUserIds = (await connection.QueryAsync<int>(
                        @"SELECT u.USER_ID 
                        FROM USERS u 
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID 
                        WHERE r.ROLE_NAME = 'ADMIN'",
                        transaction: transaction)).AsList();

                    await transaction.CommitAsync();

                    // Notify the user about their new permission
                    await _notificationService.CreateNotificationAsync(
                        userId,
                        "New Permission Granted",
                        $"You have been granted the permission: {permissionName}\n{permissionDetails.Description}",
                        "success");

                    // Notify admins about the permission grant
                    foreach (var adminId in adminUserIds)
                    {
                        await _notificationService.CreateNotificationAsync(
                            adminId,
                            "Permission Grant Notification",
                            $"User {userName} ({userId}) has been granted the permission: {permissionName}",
                            "info");
                    }

                    _logger.LogInfo("PermissionService", $"Permission {permissionName} granted to user {userId}");
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("PermissionService", $"Error in transaction: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionService", $"Error granting permission: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RevokePermissionAsync(int userId, string permissionName)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Get user's name
                    var userName = await connection.QuerySingleOrDefaultAsync<string>(
                        "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId },
                        transaction);

                    // Get the permission ID and details
                    var permissionDetails = await connection.QuerySingleOrDefaultAsync<(int Id, string Description)>(
                        "SELECT PERMISSION_ID as Id, DESCRIPTION as Description FROM PERMISSIONS WHERE PERMISSION_NAME = @PermissionName",
                        new { PermissionName = permissionName },
                        transaction);

                    if (permissionDetails.Id == 0)
                    {
                        _logger.LogWarning("PermissionService", $"Permission {permissionName} not found");
                        return false;
                    }

                    // Revoke the permission
                    var rowsAffected = await connection.ExecuteAsync(
                        "UPDATE USER_PERMISSIONS SET IS_GRANT = 0 WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId",
                        new { UserId = userId, PermissionId = permissionDetails.Id },
                        transaction);

                    // Log the change in audit
                    await connection.ExecuteAsync(@"
                        INSERT INTO PERMISSION_AUDIT_LOG (USER_ID, PERMISSION_ID, ACTION, CHANGE_DATE, REASON)
                        VALUES (@UserId, @PermissionId, 'REVOKE', GETDATE(), @Reason)",
                        new { 
                            UserId = userId, 
                            PermissionId = permissionDetails.Id,
                            Reason = $"Permission revoked: {permissionName}"
                        },
                        transaction);

                    // Get admin user IDs
                    var adminUserIds = (await connection.QueryAsync<int>(
                        @"SELECT u.USER_ID 
                        FROM USERS u 
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID 
                        WHERE r.ROLE_NAME = 'ADMIN'",
                        transaction: transaction)).AsList();

                    await transaction.CommitAsync();

                    if (rowsAffected > 0)
                    {
                        // Notify the user about the revoked permission
                        await _notificationService.CreateNotificationAsync(
                            userId,
                            "Permission Revoked",
                            $"The following permission has been revoked: {permissionName}",
                            "warning");

                        // Notify admins about the permission revocation
                        foreach (var adminId in adminUserIds)
                        {
                            await _notificationService.CreateNotificationAsync(
                                adminId,
                                "Permission Revocation Notification",
                                $"Permission {permissionName} has been revoked from user {userName} ({userId})",
                                "info");
                        }
                    }

                    _logger.LogInfo("PermissionService", $"Permission {permissionName} revoked from user {userId}");
                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("PermissionService", $"Error in transaction: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("PermissionService", $"Error revoking permission: {ex.Message}");
                return false;
            }
        }
    }
} 