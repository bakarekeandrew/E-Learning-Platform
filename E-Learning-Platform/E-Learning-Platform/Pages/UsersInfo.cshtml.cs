using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using BCrypt.Net;
using Dapper;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace E_Learning_Platform.Pages
{
    //[Authorize(Roles = "Admin")]
    public class UsersInfoModel : PageModel
    {
        private readonly string _connectionString;

        public UsersInfoModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        // Properties for view
        public List<User> Users { get; set; } = new();
        public List<SelectListItem> RoleOptions { get; set; } = new();
        public PaginationInfo Pagination { get; set; } = new();

        [BindProperty]
        public FilterOptions Filters { get; set; } = new();

        [BindProperty]
        public UserInputModel UserInput { get; set; } = new();

        // Models
        public class User
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string RoleName { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public DateTime DateRegistered { get; set; }
        }

        public class UserInputModel
        {
            public int UserId { get; set; }

            [Required]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [StringLength(100, MinimumLength = 6)]
            public string Password { get; set; } = string.Empty;

            [Required]
            public int RoleId { get; set; }

            public bool IsActive { get; set; } = true;
        }

        public class PaginationInfo
        {
            public int CurrentPage { get; set; } = 1;
            public int PageSize { get; set; } = 10;
            public int TotalItems { get; set; }
            public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
        }

        public class FilterOptions
        {
            public string SearchTerm { get; set; } = string.Empty;
            public string RoleFilter { get; set; } = string.Empty;
            public string StatusFilter { get; set; } = string.Empty;
            public string CurrentTab { get; set; } = "all";
        }

        public async Task OnGetAsync(
            string searchTerm = "",
            string roleFilter = "",
            string statusFilter = "",
            string tab = "all",
            int pageNumber = 1)
        {
            Filters.SearchTerm = searchTerm;
            Filters.RoleFilter = roleFilter;
            Filters.StatusFilter = statusFilter;
            Filters.CurrentTab = tab;
            Pagination.CurrentPage = pageNumber < 1 ? 1 : pageNumber;

            await LoadRoleOptionsAsync();
            await LoadUsersAsync();
        }

        public async Task<IActionResult> OnPostAdd()
        {
            if (!ModelState.IsValid)
            {
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if email exists
                var emailExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM USERS WHERE EMAIL = @Email",
                    new { UserInput.Email });

                if (emailExists)
                {
                    ModelState.AddModelError("UserInput.Email", "Email already registered");
                    await LoadRoleOptionsAsync();
                    await LoadUsersAsync();
                    return Page();
                }

                // Hash the password using BCrypt
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(UserInput.Password);

                // Insert new user with PASSWORD_HASH
                var query = @"
                    INSERT INTO USERS (FULL_NAME, EMAIL, PASSWORD_HASH, ROLE_ID, DATE_REGISTERED, LAST_LOGIN) 
                    VALUES (@FullName, @Email, @PasswordHash, @RoleId, @DateRegistered, @LastLogin)";

                await connection.ExecuteAsync(query, new
                {
                    UserInput.FullName,
                    UserInput.Email,
                    PasswordHash = passwordHash,
                    UserInput.RoleId,
                    DateRegistered = DateTime.Now,
                    LastLogin = UserInput.IsActive ? DateTime.Now : (DateTime?)null
                });

                TempData["SuccessMessage"] = "User added successfully!";
                return new JsonResult(new
                {
                    redirect = Url.Page("UsersInfo", new
                    {
                        searchTerm = Filters.SearchTerm,
                        roleFilter = Filters.RoleFilter,
                        statusFilter = Filters.StatusFilter,
                        tab = Filters.CurrentTab,
                        pageNumber = Pagination.CurrentPage
                    })
                });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error occurred: {ex.Message}");
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return BadRequest(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostUpdate()
        {
            if (!ModelState.IsValid)
            {
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if email exists for other users
                var emailExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM USERS WHERE EMAIL = @Email AND USER_ID <> @UserId",
                    new { UserInput.Email, UserInput.UserId });

                if (emailExists)
                {
                    ModelState.AddModelError("UserInput.Email", "Email already registered by another user");
                    await LoadRoleOptionsAsync();
                    await LoadUsersAsync();
                    return Page();
                }

                // Update user based on whether password was provided
                if (string.IsNullOrEmpty(UserInput.Password))
                {
                    // Update without changing password
                    var query = @"
                        UPDATE USERS SET 
                            FULL_NAME = @FullName, 
                            EMAIL = @Email, 
                            ROLE_ID = @RoleId,
                            LAST_LOGIN = CASE WHEN @IsActive = 1 THEN ISNULL(LAST_LOGIN, GETDATE()) ELSE NULL END
                        WHERE USER_ID = @UserId";

                    await connection.ExecuteAsync(query, new
                    {
                        UserInput.FullName,
                        UserInput.Email,
                        UserInput.RoleId,
                        UserInput.IsActive,
                        UserInput.UserId
                    });
                }
                else
                {
                    // Update with new password using PASSWORD_HASH
                    var passwordHash = BCrypt.Net.BCrypt.HashPassword(UserInput.Password);

                    var query = @"
                        UPDATE USERS SET 
                            FULL_NAME = @FullName, 
                            EMAIL = @Email, 
                            PASSWORD_HASH = @PasswordHash, 
                            ROLE_ID = @RoleId,
                            LAST_LOGIN = CASE WHEN @IsActive = 1 THEN ISNULL(LAST_LOGIN, GETDATE()) ELSE NULL END
                        WHERE USER_ID = @UserId";

                    await connection.ExecuteAsync(query, new
                    {
                        UserInput.FullName,
                        UserInput.Email,
                        PasswordHash = passwordHash,
                        UserInput.RoleId,
                        UserInput.IsActive,
                        UserInput.UserId
                    });
                }

                TempData["SuccessMessage"] = "User updated successfully!";
                return new JsonResult(new
                {
                    redirect = Url.Page("UsersInfo", new
                    {
                        searchTerm = Filters.SearchTerm,
                        roleFilter = Filters.RoleFilter,
                        statusFilter = Filters.StatusFilter,
                        tab = Filters.CurrentTab,
                        pageNumber = Pagination.CurrentPage
                    })
                });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error occurred: {ex.Message}");
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return BadRequest(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDelete(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First check if user exists
                var userExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM USERS WHERE USER_ID = @UserId",
                    new { UserId = userId });

                if (!userExists)
                {
                    return new JsonResult(new { error = "User not found" });
                }

                // Delete the user
                var affectedRows = await connection.ExecuteAsync(
                    "DELETE FROM USERS WHERE USER_ID = @UserId",
                    new { UserId = userId });

                TempData["SuccessMessage"] = "User deleted successfully!";
                return new JsonResult(new
                {
                    redirect = Url.Page("UsersInfo", new
                    {
                        searchTerm = Filters.SearchTerm,
                        roleFilter = Filters.RoleFilter,
                        statusFilter = Filters.StatusFilter,
                        tab = Filters.CurrentTab,
                        pageNumber = Pagination.CurrentPage
                    })
                });
            }
            catch (SqlException ex)
            {
                return new JsonResult(new { error = $"Database error occurred while deleting user: {ex.Message}" });
            }
        }

        public async Task<IActionResult> OnPostToggleStatus(int userId, bool currentStatus)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string query = currentStatus
                    ? "UPDATE USERS SET LAST_LOGIN = NULL WHERE USER_ID = @UserId"
                    : "UPDATE USERS SET LAST_LOGIN = @CurrentDate WHERE USER_ID = @UserId";

                await connection.ExecuteAsync(query, new
                {
                    UserId = userId,
                    CurrentDate = DateTime.Now
                });

                TempData["SuccessMessage"] = $"User status updated to {(currentStatus ? "inactive" : "active")}!";
                return RedirectToPage(new
                {
                    searchTerm = Filters.SearchTerm,
                    roleFilter = Filters.RoleFilter,
                    statusFilter = Filters.StatusFilter,
                    tab = Filters.CurrentTab,
                    pageNumber = Pagination.CurrentPage
                });
            }
            catch (SqlException ex)
            {
                TempData["ErrorMessage"] = $"Database error occurred while updating status: {ex.Message}";
                return RedirectToPage(new
                {
                    searchTerm = Filters.SearchTerm,
                    roleFilter = Filters.RoleFilter,
                    statusFilter = Filters.StatusFilter,
                    tab = Filters.CurrentTab,
                    pageNumber = Pagination.CurrentPage
                });
            }
        }

        public async Task<JsonResult> OnGetUserById(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        USER_ID as UserId,
                        FULL_NAME as FullName,
                        EMAIL as Email,
                        ROLE_ID as RoleId,
                        CASE WHEN LAST_LOGIN IS NOT NULL THEN 1 ELSE 0 END as IsActive
                    FROM USERS
                    WHERE USER_ID = @UserId";

                var user = await connection.QueryFirstOrDefaultAsync<UserInputModel>(
                    query, new { UserId = userId });

                if (user == null)
                {
                    return new JsonResult(new { error = "User not found" });
                }

                return new JsonResult(user);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = $"An error occurred while fetching user data: {ex.Message}" });
            }
        }

        private async Task LoadUsersAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Build where clause
            var whereClauses = new List<string> { "1=1" };
            var parameters = new DynamicParameters();

            // Apply tab filter
            if (Filters.CurrentTab != "all")
            {
                whereClauses.Add("r.ROLE_NAME = @RoleName");
                parameters.Add("RoleName", Filters.CurrentTab);
            }

            // Apply search term
            if (!string.IsNullOrEmpty(Filters.SearchTerm))
            {
                whereClauses.Add("(u.FULL_NAME LIKE @SearchTerm OR u.EMAIL LIKE @SearchTerm)");
                parameters.Add("SearchTerm", $"%{Filters.SearchTerm}%");
            }

            // Apply role filter
            if (!string.IsNullOrEmpty(Filters.RoleFilter))
            {
                whereClauses.Add("r.ROLE_NAME = @RoleFilter");
                parameters.Add("RoleFilter", Filters.RoleFilter);
            }

            // Apply status filter
            if (!string.IsNullOrEmpty(Filters.StatusFilter))
            {
                whereClauses.Add(Filters.StatusFilter.ToLower() == "active"
                    ? "u.LAST_LOGIN IS NOT NULL"
                    : "u.LAST_LOGIN IS NULL");
            }

            var whereClause = string.Join(" AND ", whereClauses);

            // Count total users
            var countQuery = $@"
                SELECT COUNT(*)
                FROM USERS u
                JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                WHERE {whereClause}";

            Pagination.TotalItems = await connection.ExecuteScalarAsync<int>(countQuery, parameters);

            // Get paginated users
            var offset = (Pagination.CurrentPage - 1) * Pagination.PageSize;
            parameters.Add("Offset", offset);
            parameters.Add("PageSize", Pagination.PageSize);

            var selectQuery = $@"
                SELECT
                    u.USER_ID as UserId,
                    u.FULL_NAME as FullName,
                    u.EMAIL as Email,
                    r.ROLE_NAME as RoleName,
                    CASE WHEN u.LAST_LOGIN IS NOT NULL THEN 1 ELSE 0 END as IsActive,
                    u.DATE_REGISTERED as DateRegistered
                FROM USERS u
                JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                WHERE {whereClause}
                ORDER BY u.DATE_REGISTERED DESC
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            Users = (await connection.QueryAsync<User>(selectQuery, parameters)).ToList();
        }

        private async Task LoadRoleOptionsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT ROLE_ID as Value, ROLE_NAME as Text FROM ROLES ORDER BY ROLE_NAME";
            var roles = await connection.QueryAsync<RoleOption>(query);

            RoleOptions = roles.Select(r => new SelectListItem
            {
                Value = r.Value.ToString(),
                Text = r.Text
            }).ToList();
        }

        private class RoleOption
        {
            public int Value { get; set; }
            public string Text { get; set; } = string.Empty;
        }
    }
}