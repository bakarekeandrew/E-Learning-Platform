using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace E_Learning_Platform.Pages.Admin
{
    [Authorize(Roles = "ADMIN")]
    public class RoleManagementModel : PageModel
    {
        private readonly string _connectionString;

        public RoleManagementModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        public List<Role> Roles { get; set; } = new();

        public class Role
        {
            public int RoleId { get; set; }
            public string RoleName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadRolesAsync();
        }

        public async Task<IActionResult> OnPostCreateRoleAsync(string roleName, string description)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.ExecuteAsync(
                    "INSERT INTO ROLES (ROLE_NAME, DESCRIPTION, CREATED_DATE) VALUES (@RoleName, @Description, GETDATE())",
                    new { RoleName = roleName.ToUpper(), Description = description });

                TempData["SuccessMessage"] = "Role created successfully";
            }
            catch (SqlException ex)
            {
                TempData["ErrorMessage"] = $"Error creating role: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateRoleAsync(int roleId, string roleName, string description)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.ExecuteAsync(
                    "UPDATE ROLES SET ROLE_NAME = @RoleName, DESCRIPTION = @Description WHERE ROLE_ID = @RoleId",
                    new { RoleName = roleName.ToUpper(), Description = description, RoleId = roleId });

                TempData["SuccessMessage"] = "Role updated successfully";
            }
            catch (SqlException ex)
            {
                TempData["ErrorMessage"] = $"Error updating role: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteRoleAsync(int roleId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                // Check if any users have this role
                var userCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM USERS WHERE ROLE_ID = @RoleId",
                    new { RoleId = roleId });

                if (userCount > 0)
                {
                    TempData["ErrorMessage"] = "Cannot delete role assigned to users";
                    return RedirectToPage();
                }

                await connection.ExecuteAsync(
                    "DELETE FROM ROLES WHERE ROLE_ID = @RoleId",
                    new { RoleId = roleId });

                TempData["SuccessMessage"] = "Role deleted successfully";
            }
            catch (SqlException ex)
            {
                TempData["ErrorMessage"] = $"Error deleting role: {ex.Message}";
            }

            return RedirectToPage();
        }

        private async Task LoadRolesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            Roles = (await connection.QueryAsync<Role>(
                "SELECT ROLE_ID as RoleId, ROLE_NAME as RoleName, DESCRIPTION as Description, CREATED_DATE as CreatedDate FROM ROLES ORDER BY ROLE_NAME"))
                .ToList();
        }
    }
}