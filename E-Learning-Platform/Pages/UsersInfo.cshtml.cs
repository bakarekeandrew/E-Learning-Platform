using System.Collections.Generic;
using System.Threading.Tasks;
using E_Learning_Platform.Models;
using E_Learning_Platform.Pages.Services;
using E_Learning_Platform.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Linq;

namespace E_Learning_Platform.Pages
{
    public class UsersInfoModel : BasePageModel
    {
        private readonly string _connectionString;
        public List<UserInfo> Users { get; set; }

        public UsersInfoModel(IPermissionService permissionService, IConfiguration configuration)
            : base(permissionService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadUserPermissionsAsync();

            if (!HasPermission("USER.VIEW"))
            {
                return Forbid();
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get basic user information
            var users = await connection.QueryAsync<UserInfo>(@"
                SELECT 
                    U.USER_ID, 
                    U.USERNAME, 
                    U.EMAIL, 
                    U.FULL_NAME, 
                    U.DATE_REGISTERED as CREATED_DATE, 
                    U.IS_ACTIVE,
                    U.ROLE_ID,
                    R.ROLE_NAME
                FROM USERS U
                LEFT JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID
                WHERE U.IS_ACTIVE = 1
                ORDER BY U.FULL_NAME");

            Users = users.AsList();

            // Get permissions for each user
            foreach (var user in Users)
            {
                var permissions = await connection.QueryAsync<UserPermissionInfo>(@"
                    SELECT 
                        P.PERMISSION_ID,
                        P.PERMISSION_NAME,
                        P.DESCRIPTION,
                        PC.CATEGORY_NAME,
                        UP.ASSIGNED_DATE,
                        U.FULL_NAME as ASSIGNED_BY,
                        UP.IS_GRANT,
                        UP.EXPIRATION_DATE
                    FROM USER_PERMISSIONS UP
                    JOIN PERMISSIONS P ON UP.PERMISSION_ID = P.PERMISSION_ID
                    JOIN PERMISSION_CATEGORIES PC ON P.CATEGORY_ID = PC.CATEGORY_ID
                    JOIN USERS U ON UP.ASSIGNED_BY = U.USER_ID
                    WHERE UP.USER_ID = @UserId AND UP.IS_GRANT = 1
                    ORDER BY PC.CATEGORY_NAME, P.PERMISSION_NAME",
                    new { UserId = user.USER_ID });

                user.Permissions = permissions.ToList();
                
                // Set permission flags
                user.HasManagePermissions = user.Permissions.Any(p => p.PERMISSION_NAME.EndsWith(".MANAGE"));
                user.HasViewPermissions = user.Permissions.Any(p => p.PERMISSION_NAME.EndsWith(".VIEW"));
            }

            return Page();
        }
    }
} 