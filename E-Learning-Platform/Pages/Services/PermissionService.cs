using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using Dapper;

namespace E_Learning_Platform.Pages.Services
{
    public interface IPermissionService
    {
        Task<bool> HasPermissionAsync(int userId, string permission);
        Task<List<string>> GetUserPermissionsAsync(int userId);
        Task<bool> HasAnyPermissionAsync(int userId, params string[] permissions);
    }

    public class PermissionService : IPermissionService
    {
        private readonly string _connectionString;
        private readonly IMemoryCache _cache;
        private const int CACHE_DURATION_MINUTES = 10;

        public PermissionService(IConfiguration configuration, IMemoryCache cache)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _cache = cache;
        }

        public async Task<bool> HasPermissionAsync(int userId, string permission)
        {
            // Check cache first
            string cacheKey = $"user_permission_{userId}_{permission}";
            if (_cache.TryGetValue(cacheKey, out bool hasPermission))
            {
                return hasPermission;
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // First check if user is admin
            var adminQuery = @"
                SELECT COUNT(1)
                FROM USERS U
                JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID
                WHERE U.USER_ID = @UserId
                AND R.ROLE_NAME = 'ADMIN'";

            var isAdmin = await connection.ExecuteScalarAsync<int>(adminQuery, new { UserId = userId }) > 0;
            if (isAdmin)
            {
                _cache.Set(cacheKey, true, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
                return true;
            }

            // Check for specific permission including hierarchy
            var permissionQuery = @"
                WITH RECURSIVE_PERMISSIONS AS (
                    -- Get directly assigned permissions
                    SELECT P.PERMISSION_ID, P.PERMISSION_NAME
                    FROM PERMISSIONS P
                    WHERE P.PERMISSION_NAME = @Permission
                    
                    UNION ALL
                    
                    -- Get parent permissions through hierarchy
                    SELECT P.PERMISSION_ID, P.PERMISSION_NAME
                    FROM PERMISSIONS P
                    INNER JOIN PERMISSION_HIERARCHY PH ON P.PERMISSION_ID = PH.PARENT_PERMISSION_ID
                    INNER JOIN RECURSIVE_PERMISSIONS RP ON PH.CHILD_PERMISSION_ID = RP.PERMISSION_ID
                )
                SELECT COUNT(1)
                FROM (
                    -- Check user direct permissions
                    SELECT 1
                    FROM USER_PERMISSIONS UP
                    JOIN RECURSIVE_PERMISSIONS RP ON UP.PERMISSION_ID = RP.PERMISSION_ID
                    WHERE UP.USER_ID = @UserId
                    AND UP.IS_GRANT = 1
                    AND (UP.EXPIRATION_DATE IS NULL OR UP.EXPIRATION_DATE > GETDATE())
                    
                    UNION
                    
                    -- Check role-based permissions
                    SELECT 1
                    FROM USERS U
                    JOIN ROLE_PERMISSIONS RP ON U.ROLE_ID = RP.ROLE_ID
                    JOIN RECURSIVE_PERMISSIONS RPerm ON RP.PERMISSION_ID = RPerm.PERMISSION_ID
                    WHERE U.USER_ID = @UserId
                ) AS PermissionCheck";

            var hasPermissionResult = await connection.ExecuteScalarAsync<int>(
                permissionQuery,
                new { UserId = userId, Permission = permission }) > 0;

            _cache.Set(cacheKey, hasPermissionResult, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
            return hasPermissionResult;
        }

        public async Task<List<string>> GetUserPermissionsAsync(int userId)
        {
            string cacheKey = $"user_permissions_{userId}";
            if (_cache.TryGetValue(cacheKey, out List<string> permissions))
            {
                return permissions;
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                WITH RECURSIVE_PERMISSIONS AS (
                    -- Get all permissions
                    SELECT P.PERMISSION_ID, P.PERMISSION_NAME
                    FROM PERMISSIONS P
                    
                    UNION ALL
                    
                    -- Get child permissions through hierarchy
                    SELECT P.PERMISSION_ID, P.PERMISSION_NAME
                    FROM PERMISSIONS P
                    INNER JOIN PERMISSION_HIERARCHY PH ON P.PERMISSION_ID = PH.CHILD_PERMISSION_ID
                    INNER JOIN RECURSIVE_PERMISSIONS RP ON PH.PARENT_PERMISSION_ID = RP.PERMISSION_ID
                )
                SELECT DISTINCT P.PERMISSION_NAME
                FROM (
                    -- Get user direct permissions
                    SELECT RP.PERMISSION_NAME
                    FROM USER_PERMISSIONS UP
                    JOIN RECURSIVE_PERMISSIONS RP ON UP.PERMISSION_ID = RP.PERMISSION_ID
                    WHERE UP.USER_ID = @UserId
                    AND UP.IS_GRANT = 1
                    AND (UP.EXPIRATION_DATE IS NULL OR UP.EXPIRATION_DATE > GETDATE())
                    
                    UNION
                    
                    -- Get role-based permissions
                    SELECT RP.PERMISSION_NAME
                    FROM USERS U
                    JOIN ROLE_PERMISSIONS RP ON U.ROLE_ID = RP.ROLE_ID
                    JOIN RECURSIVE_PERMISSIONS RP ON RP.PERMISSION_ID = RP.PERMISSION_ID
                    WHERE U.USER_ID = @UserId
                ) AS P";

            var result = (await connection.QueryAsync<string>(query, new { UserId = userId })).ToList();
            
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));
            return result;
        }

        public async Task<bool> HasAnyPermissionAsync(int userId, params string[] permissions)
        {
            foreach (var permission in permissions)
            {
                if (await HasPermissionAsync(userId, permission))
                {
                    return true;
                }
            }
            return false;
        }
    }
} 