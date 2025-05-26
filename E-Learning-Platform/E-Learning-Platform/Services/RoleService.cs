using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Linq;

namespace E_Learning_Platform.Services
{
    public class RoleService : IRoleService
    {
        private readonly string _connectionString;
        private readonly ILogger<RoleService> _logger;
        private readonly INotificationEventService _notificationEventService;

        public RoleService(
            IConfiguration configuration,
            ILogger<RoleService> logger,
            INotificationEventService notificationEventService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _notificationEventService = notificationEventService;
        }

        public async Task<int> GetDefaultRoleIdAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                return await connection.QueryFirstOrDefaultAsync<int>(
                    "SELECT ROLE_ID FROM ROLES WHERE ROLE_NAME = 'STUDENT'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting default role ID");
                return -1;
            }
        }

        public async Task<string> GetRoleNameAsync(int roleId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                return await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT ROLE_NAME FROM ROLES WHERE ROLE_ID = @RoleId",
                    new { RoleId = roleId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting role name for ID {RoleId}", roleId);
                return null;
            }
        }

        public async Task<IEnumerable<(int Id, string Name)>> GetAllRolesAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var roles = await connection.QueryAsync<(int Id, string Name)>(
                    "SELECT ROLE_ID as Id, ROLE_NAME as Name FROM ROLES ORDER BY ROLE_NAME");
                return roles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all roles");
                return new List<(int Id, string Name)>();
            }
        }

        public async Task<bool> AssignRoleAsync(int userId, int roleId)
        {
            _logger.LogInformation("[RoleService] Starting role assignment. UserId: {UserId}, RoleId: {RoleId}", 
                userId, roleId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("[RoleService] Database connection opened successfully");

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Get current role name
                    var oldRoleName = await connection.QuerySingleOrDefaultAsync<string>(
                        @"SELECT r.ROLE_NAME 
                        FROM USERS u 
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID 
                        WHERE u.USER_ID = @UserId",
                        new { UserId = userId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Retrieved old role name: {OldRoleName}", oldRoleName);

                    // Get new role name
                    var newRoleName = await connection.QuerySingleOrDefaultAsync<string>(
                        "SELECT ROLE_NAME FROM ROLES WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Retrieved new role name: {NewRoleName}", newRoleName);

                    if (string.IsNullOrEmpty(newRoleName))
                    {
                        _logger.LogWarning("[RoleService] New role not found for RoleId: {RoleId}", roleId);
                        return false;
                    }

                    // Update user's role
                    var rowsAffected = await connection.ExecuteAsync(
                        "UPDATE USERS SET ROLE_ID = @RoleId WHERE USER_ID = @UserId",
                        new { RoleId = roleId, UserId = userId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Role update affected {RowsAffected} rows", rowsAffected);

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning("[RoleService] No rows affected when updating role for user {UserId}", userId);
                        await transaction.RollbackAsync();
                        return false;
                    }

                    await transaction.CommitAsync();
                    _logger.LogDebug("[RoleService] Transaction committed successfully");

                    _logger.LogInformation("[RoleService] Role {NewRoleName} successfully assigned to user {UserId}", 
                        newRoleName, userId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RoleService] Error in transaction. Rolling back...");
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RoleService] Error assigning role to user {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> RemoveRoleAsync(int userId, int roleId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var rowsAffected = await connection.ExecuteAsync(
                    "UPDATE USERS SET ROLE_ID = NULL WHERE USER_ID = @UserId AND ROLE_ID = @RoleId",
                    new { UserId = userId, RoleId = roleId });
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing role {RoleId} from user {UserId}", roleId, userId);
                return false;
            }
        }

        public async Task<bool> HasRoleAsync(int userId, string roleName)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var hasRole = await connection.QueryFirstOrDefaultAsync<bool>(
                    @"SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM USERS u
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                        WHERE u.USER_ID = @UserId AND r.ROLE_NAME = @RoleName
                    ) THEN 1 ELSE 0 END",
                    new { UserId = userId, RoleName = roleName });
                return hasRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user {UserId} has role {RoleName}", userId, roleName);
                return false;
            }
        }

        public async Task AssignRoleToUserAsync(int userId, int roleId, int assignedBy, string reason)
        {
            _logger.LogInformation("[RoleService] Starting role assignment. UserId: {UserId}, RoleId: {RoleId}, AssignedBy: {AssignedBy}, Reason: {Reason}", 
                userId, roleId, assignedBy, reason);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("[RoleService] Database connection opened successfully");

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Get current role name
                    var oldRoleName = await connection.QuerySingleOrDefaultAsync<string>(
                        @"SELECT r.ROLE_NAME 
                        FROM USERS u 
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID 
                        WHERE u.USER_ID = @UserId",
                        new { UserId = userId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Retrieved old role name: {OldRoleName}", oldRoleName);

                    // Get new role name
                    var newRoleName = await connection.QuerySingleOrDefaultAsync<string>(
                        "SELECT ROLE_NAME FROM ROLES WHERE ROLE_ID = @RoleId",
                        new { RoleId = roleId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Retrieved new role name: {NewRoleName}", newRoleName);

                    if (string.IsNullOrEmpty(newRoleName))
                    {
                        _logger.LogWarning("[RoleService] New role not found for RoleId: {RoleId}", roleId);
                        return;
                    }

                    // Get user name
                    var userName = await connection.QuerySingleOrDefaultAsync<string>(
                        "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Retrieved user name: {UserName}", userName);

                    // Update user's role
                    var rowsAffected = await connection.ExecuteAsync(
                        "UPDATE USERS SET ROLE_ID = @RoleId WHERE USER_ID = @UserId",
                        new { RoleId = roleId, UserId = userId },
                        transaction: transaction);

                    _logger.LogDebug("[RoleService] Role update affected {RowsAffected} rows", rowsAffected);

                    if (rowsAffected == 0)
                    {
                        _logger.LogWarning("[RoleService] No rows affected when updating role for user {UserId}", userId);
                        await transaction.RollbackAsync();
                        return;
                    }

                    // Get admin user IDs
                    var adminUserIds = await connection.QueryAsync<int>(
                        @"SELECT u.USER_ID 
                        FROM USERS u 
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID 
                        WHERE r.ROLE_NAME = 'ADMIN'",
                        transaction: transaction);

                    await transaction.CommitAsync();
                    _logger.LogDebug("[RoleService] Transaction committed successfully");

                    // Raise notification events
                    try
                    {
                        // Notify the user about their new role
                        _logger.LogDebug("[RoleService] Preparing notification event for user {UserId}", userId);
                        var userNotification = new NotificationEvent
                        {
                            UserId = userId,
                            Title = "Role Updated",
                            Message = $"Your role has been changed from {oldRoleName} to {newRoleName}\nReason: {reason}",
                            Type = "info"
                        };

                        _logger.LogDebug("[RoleService] Raising notification event for user {UserId}. Title: {Title}", userId, userNotification.Title);
                        _notificationEventService.RaiseEvent(userNotification);
                        _logger.LogInformation("[RoleService] Successfully raised notification event for user {UserId}", userId);

                        // Notify all admins about the role change
                        foreach (var adminId in adminUserIds)
                        {
                            if (adminId != assignedBy) // Don't notify the admin who made the change
                            {
                                _logger.LogDebug("[RoleService] Preparing notification event for admin {AdminId}", adminId);
                                var adminNotification = new NotificationEvent
                                {
                                    UserId = adminId,
                                    Title = "Role Change Notification",
                                    Message = $"User {userName} ({userId}) has been assigned the role {newRoleName} by Admin ID: {assignedBy}\nReason: {reason}",
                                    Type = "info"
                                };

                                _logger.LogDebug("[RoleService] Raising notification event for admin {AdminId}. Title: {Title}", adminId, adminNotification.Title);
                                _notificationEventService.RaiseEvent(adminNotification);
                                _logger.LogInformation("[RoleService] Successfully raised notification event for admin {AdminId}", adminId);
                            }
                        }
                        _logger.LogInformation("[RoleService] All notification events raised successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[RoleService] Error raising notification events. Role change was successful but notifications failed. Details: {ErrorMessage}", ex.Message);
                        // Don't throw here as the role change was successful
                    }

                    _logger.LogInformation("[RoleService] Role {NewRoleName} successfully assigned to user {UserId} by {AssignedBy}", 
                        newRoleName, userId, assignedBy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[RoleService] Error in transaction. Rolling back...");
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RoleService] Error assigning role to user {UserId}", userId);
                throw;
            }
        }

        public async Task<(int RoleId, string RoleName)> GetUserRoleAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var result = await connection.QueryFirstOrDefaultAsync<(int RoleId, string RoleName)>(
                    @"SELECT r.ROLE_ID as RoleId, r.ROLE_NAME as RoleName
                    FROM USERS u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE u.USER_ID = @UserId",
                    new { UserId = userId });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting role for user {UserId}", userId);
                return (0, string.Empty);
            }
        }

        public async Task<IEnumerable<int>> GetUsersInRoleAsync(int roleId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var users = (await connection.QueryAsync<int>(@"
                    SELECT u.USER_ID
                    FROM USERS u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE r.ROLE_ID = @RoleId",
                    new { RoleId = roleId })).AsList();

                _logger.LogInformation("Retrieved {Count} users for role {RoleId}", users.Count, roleId);
                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users for role {RoleId}", roleId);
                return Enumerable.Empty<int>();
            }
        }
    }
} 