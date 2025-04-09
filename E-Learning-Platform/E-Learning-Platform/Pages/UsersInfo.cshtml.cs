using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace E_Learning_Platform.Pages
{
    public class UsersInfoModel : PageModel
    {
        // Properties for the view
        public List<User> Users { get; set; } = new List<User>();
        public int TotalUsers { get; set; } = 0;
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalPages => (int)Math.Ceiling(TotalUsers / (double)PageSize);
        public string SearchTerm { get; set; } = string.Empty;
        public string RoleFilter { get; set; } = string.Empty;
        public string StatusFilter { get; set; } = string.Empty;
        public string CurrentTab { get; set; } = "all";

        // Define possible role options
        public List<string> RoleOptions { get; set; } = new List<string> { "Instructor", "Student", "Admin" };

        // User model
        public class User
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string RoleName { get; set; } = string.Empty;
            public bool IsActive { get; set; }
            public DateTime DateRegistered { get; set; }
        }

        // User model for adding/editing
        [BindProperty]
        public UserInputModel UserInput { get; set; } = new UserInputModel();

        public class UserInputModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public int RoleId { get; set; }
            public bool IsActive { get; set; } = true;
        }

        // Connection string
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                          "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                          "Integrated Security=True;" +
                                          "TrustServerCertificate=True";

        // GET handler with search, filtering and pagination
        public async Task OnGetAsync(string searchTerm = "", string roleFilter = "", string statusFilter = "", string tab = "all", int pageNumber = 1)
        {
            // Store filter values for the view
            SearchTerm = searchTerm ?? string.Empty;
            RoleFilter = roleFilter ?? string.Empty;
            StatusFilter = statusFilter ?? string.Empty;
            CurrentTab = tab ?? "all";
            CurrentPage = pageNumber < 1 ? 1 : pageNumber;

            try
            {
                await LoadUsersAsync();
            }
            catch (SqlException sqlEx)
            {
                HandleSqlException(sqlEx);
            }
            catch (Exception ex)
            {
                HandleGeneralException(ex);
            }
        }

        // POST handler for adding a new user
        public async Task<IActionResult> OnPostAddUserAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadUsersAsync();
                return Page();
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Check if the email already exists
                    string checkEmailQuery = "SELECT COUNT(*) FROM USERS WHERE EMAIL = @Email";
                    using (SqlCommand checkCommand = new SqlCommand(checkEmailQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@Email", UserInput.Email);
                        int emailCount = (int)await checkCommand.ExecuteScalarAsync();

                        if (emailCount > 0)
                        {
                            ModelState.AddModelError("UserInput.Email", "This email is already registered");
                            await LoadUsersAsync();
                            return Page();
                        }
                    }

                    // Add the new user
                    string insertQuery = @"
                        INSERT INTO USERS (FULL_NAME, EMAIL, PASSWORD, ROLE_ID, DATE_REGISTERED) 
                        VALUES (@FullName, @Email, @Password, @RoleId, @DateRegistered);
                        SELECT SCOPE_IDENTITY();";

                    using (SqlCommand command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", UserInput.FullName);
                        command.Parameters.AddWithValue("@Email", UserInput.Email);
                        command.Parameters.AddWithValue("@Password", HashPassword(UserInput.Password)); // You should implement a proper password hashing function
                        command.Parameters.AddWithValue("@RoleId", UserInput.RoleId);
                        command.Parameters.AddWithValue("@DateRegistered", DateTime.Now);

                        int newUserId = Convert.ToInt32(await command.ExecuteScalarAsync());
                        TempData["SuccessMessage"] = "User added successfully!";
                    }
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                HandleSqlException(sqlEx);
                await LoadUsersAsync();
                return Page();
            }
            catch (Exception ex)
            {
                HandleGeneralException(ex);
                await LoadUsersAsync();
                return Page();
            }
        }

        // POST handler for updating an existing user
        public async Task<IActionResult> OnPostUpdateUserAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadUsersAsync();
                return Page();
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Check if email exists for other users
                    string checkEmailQuery = "SELECT COUNT(*) FROM USERS WHERE EMAIL = @Email AND USER_ID <> @UserId";
                    using (SqlCommand checkCommand = new SqlCommand(checkEmailQuery, connection))
                    {
                        checkCommand.Parameters.AddWithValue("@Email", UserInput.Email);
                        checkCommand.Parameters.AddWithValue("@UserId", UserInput.UserId);
                        int emailCount = (int)await checkCommand.ExecuteScalarAsync();

                        if (emailCount > 0)
                        {
                            ModelState.AddModelError("UserInput.Email", "This email is already registered by another user");
                            await LoadUsersAsync();
                            return Page();
                        }
                    }

                    // Update the user
                    string updateQuery = @"
                        UPDATE USERS 
                        SET FULL_NAME = @FullName, 
                            EMAIL = @Email, 
                            ROLE_ID = @RoleId
                        WHERE USER_ID = @UserId";

                    // If password is provided, update it too
                    if (!string.IsNullOrEmpty(UserInput.Password))
                    {
                        updateQuery = @"
                            UPDATE USERS 
                            SET FULL_NAME = @FullName, 
                                EMAIL = @Email, 
                                PASSWORD = @Password, 
                                ROLE_ID = @RoleId
                            WHERE USER_ID = @UserId";
                    }

                    using (SqlCommand command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@FullName", UserInput.FullName);
                        command.Parameters.AddWithValue("@Email", UserInput.Email);
                        command.Parameters.AddWithValue("@RoleId", UserInput.RoleId);
                        command.Parameters.AddWithValue("@UserId", UserInput.UserId);

                        if (!string.IsNullOrEmpty(UserInput.Password))
                        {
                            command.Parameters.AddWithValue("@Password", HashPassword(UserInput.Password));
                        }

                        await command.ExecuteNonQueryAsync();
                        TempData["SuccessMessage"] = "User updated successfully!";
                    }
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                HandleSqlException(sqlEx);
                await LoadUsersAsync();
                return Page();
            }
            catch (Exception ex)
            {
                HandleGeneralException(ex);
                await LoadUsersAsync();
                return Page();
            }
        }

        // POST handler for deleting a user
        public async Task<IActionResult> OnPostDeleteUserAsync(int userId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Delete the user
                    string deleteQuery = "DELETE FROM USERS WHERE USER_ID = @UserId";
                    using (SqlCommand command = new SqlCommand(deleteQuery, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        int rowsAffected = await command.ExecuteNonQueryAsync();

                        if (rowsAffected > 0)
                        {
                            TempData["SuccessMessage"] = "User deleted successfully!";
                        }
                        else
                        {
                            TempData["ErrorMessage"] = "User not found or could not be deleted.";
                        }
                    }
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                HandleSqlException(sqlEx);
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                HandleGeneralException(ex);
                return RedirectToPage();
            }
        }

        // POST handler for toggling user active status
        public async Task<IActionResult> OnPostToggleStatusAsync(int userId, bool currentStatus)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    // Update the user status by updating the LAST_LOGIN field
                    string updateQuery = "";
                    if (currentStatus) // If currently active, set to inactive
                    {
                        updateQuery = "UPDATE USERS SET LAST_LOGIN = NULL WHERE USER_ID = @UserId";
                    }
                    else // If currently inactive, set to active with current date
                    {
                        updateQuery = "UPDATE USERS SET LAST_LOGIN = @CurrentDate WHERE USER_ID = @UserId";
                    }

                    using (SqlCommand command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        if (!currentStatus)
                        {
                            command.Parameters.AddWithValue("@CurrentDate", DateTime.Now);
                        }

                        await command.ExecuteNonQueryAsync();
                        TempData["SuccessMessage"] = $"User status updated to {(currentStatus ? "inactive" : "active")}!";
                    }
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                HandleSqlException(sqlEx);
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                HandleGeneralException(ex);
                return RedirectToPage();
            }
        }

        // Get user by ID for editing
        public async Task<JsonResult> OnGetUserByIdAsync(int userId)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();

                    string query = @"
                        SELECT 
                            u.USER_ID,
                            u.FULL_NAME,
                            u.EMAIL,
                            u.ROLE_ID,
                            CASE WHEN u.LAST_LOGIN IS NOT NULL THEN 1 ELSE 0 END AS IsActive
                        FROM USERS u
                        WHERE u.USER_ID = @UserId";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var user = new UserInputModel
                                {
                                    UserId = reader.GetInt32(0),
                                    FullName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                    Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                    RoleId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                                    IsActive = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                                    // Password is not returned for security reasons
                                };

                                return new JsonResult(user);
                            }
                        }
                    }
                }

                return new JsonResult(new { error = "User not found" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // Helper method to load users with filtering and pagination
        private async Task LoadUsersAsync()
        {
            using (SqlConnection connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();

                // Base WHERE clause for filtering
                string whereClause = "1=1"; // Always true to simplify building the query

                // Apply tab filter
                if (CurrentTab != "all")
                {
                    whereClause += $" AND r.ROLE_NAME = '{CurrentTab.Substring(0, 1).ToUpper() + CurrentTab.Substring(1)}'";
                }

                // Apply search term
                if (!string.IsNullOrEmpty(SearchTerm))
                {
                    whereClause += $" AND (u.FULL_NAME LIKE '%{SearchTerm}%' OR u.EMAIL LIKE '%{SearchTerm}%')";
                }

                // Apply role filter
                if (!string.IsNullOrEmpty(RoleFilter))
                {
                    whereClause += $" AND r.ROLE_NAME = '{RoleFilter}'";
                }

                // Apply status filter
                if (!string.IsNullOrEmpty(StatusFilter))
                {
                    if (StatusFilter.ToLower() == "active")
                    {
                        whereClause += " AND u.LAST_LOGIN IS NOT NULL";
                    }
                    else if (StatusFilter.ToLower() == "inactive")
                    {
                        whereClause += " AND u.LAST_LOGIN IS NULL";
                    }
                }

                // Count total users for pagination
                string countQuery = $@"
                    SELECT COUNT(*)
                    FROM USERS u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE {whereClause}";

                using (SqlCommand countCommand = new SqlCommand(countQuery, connection))
                {
                    TotalUsers = (int)await countCommand.ExecuteScalarAsync();
                }

                // Calculate pagination values
                int offset = (CurrentPage - 1) * PageSize;

                // Main query with pagination
                string query = $@"
                    SELECT
                        u.USER_ID,
                        u.FULL_NAME,
                        u.EMAIL,
                        r.ROLE_NAME,
                        CASE WHEN u.LAST_LOGIN IS NOT NULL THEN 1 ELSE 0 END AS IsActive,
                        u.DATE_REGISTERED
                    FROM USERS u
                    JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                    WHERE {whereClause}
                    ORDER BY u.DATE_REGISTERED DESC
                    OFFSET {offset} ROWS
                    FETCH NEXT {PageSize} ROWS ONLY";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        Users.Clear();
                        while (await reader.ReadAsync())
                        {
                            Users.Add(new User
                            {
                                UserId = reader.GetInt32(0),
                                FullName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                                Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                                RoleName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                                IsActive = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                                DateRegistered = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5)
                            });
                        }
                    }
                }

                // Load roles for dropdown
                string rolesQuery = "SELECT ROLE_ID, ROLE_NAME FROM ROLES ORDER BY ROLE_NAME";
                using (SqlCommand rolesCommand = new SqlCommand(rolesQuery, connection))
                {
                    using (SqlDataReader reader = await rolesCommand.ExecuteReaderAsync())
                    {
                        RoleOptions.Clear();
                        while (await reader.ReadAsync())
                        {
                            string roleName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                            RoleOptions.Add(roleName);
                        }
                    }
                }
            }
        }

        // Helper method to handle SQL exceptions
        private void HandleSqlException(SqlException sqlEx)
        {
            ViewData["ErrorMessage"] = $"SQL Error: {sqlEx.Message}, Number: {sqlEx.Number}";
            Console.WriteLine($"SQL Error: {sqlEx.Message}");
            Console.WriteLine($"Error Number: {sqlEx.Number}");
            Console.WriteLine($"Stack Trace: {sqlEx.StackTrace}");
            ModelState.AddModelError("", $"Database error: {sqlEx.Message}");
        }

        // Helper method to handle general exceptions
        private void HandleGeneralException(Exception ex)
        {
            ViewData["ErrorMessage"] = $"General Error: {ex.Message}";
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            ModelState.AddModelError("", $"An error occurred: {ex.Message}");
        }

        // Helper method to hash passwords (you should use a more secure method in production)
        private string HashPassword(string password)
        {
            // This is a very basic hash - in a real application, use a proper password hashing library
            // like BCrypt.Net, Microsoft.AspNetCore.Identity, or similar
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }
}