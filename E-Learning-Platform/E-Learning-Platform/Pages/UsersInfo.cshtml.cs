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
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using E_Learning_Platform.Services;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages
{
    [Authorize]
    public class UsersInfoModel : PageModel
    {
        private readonly string _connectionString;
        private readonly IPermissionService _permissionService;
        private readonly ILoggingService _logger;
        private readonly IAuthorizationService _authorizationService;

        public UsersInfoModel(
            IConfiguration configuration, 
            IPermissionService permissionService, 
            ILoggingService logger,
            IAuthorizationService authorizationService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _permissionService = permissionService;
            _logger = logger;
            _authorizationService = authorizationService;
        }

        // Properties for view
        public List<UserViewModel> Users { get; set; } = new();
        public List<SelectListItem> RoleOptions { get; set; } = new();
        public PaginationInfo Pagination { get; set; } = new();
        public List<AuditLog> AuditLogs { get; set; } = new();
        
        // Permission flags for UI control
        public bool CanViewUsers { get; private set; }
        public bool CanManageUsers { get; private set; }
        public bool CanCreateUsers { get; private set; }
        public bool CanEditUsers { get; private set; }
        public bool CanDeleteUsers { get; private set; }

        [BindProperty]
        public FilterModel Filters { get; set; } = new();

        [BindProperty]
        public UserInputModel UserInput { get; set; } = new();

        // Add these properties to the UsersInfoModel class
        public int SelectedUserId { get; set; }
        public List<UserPermissionViewModel> SelectedUserPermissions { get; set; } = new();
        public List<PermissionViewModel> AvailablePermissions { get; set; } = new();
        public List<PermissionHistoryEntry> PermissionHistory { get; set; } = new();

        // Models
        public class UserViewModel
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

        public class FilterModel
        {
            public string SearchTerm { get; set; } = string.Empty;
            public string RoleFilter { get; set; } = string.Empty;
            public string StatusFilter { get; set; } = string.Empty;
            public string CurrentTab { get; set; } = "all";
        }

        public class AuditLog
        {
            public DateTime ChangeDate { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public string ChangedByName { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }

        public class UserPermissionViewModel
        {
            public int PermissionId { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public string CategoryName { get; set; } = string.Empty;
            public DateTime AssignedDate { get; set; }
            public string AssignedByName { get; set; } = string.Empty;
        }

        public class PermissionViewModel
        {
            public int PermissionId { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class PermissionHistoryEntry
        {
            public DateTime ChangeDate { get; set; }
            public string PermissionName { get; set; } = string.Empty;
            public string ChangeType { get; set; } = string.Empty;
            public string ChangedByName { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }

        private async Task LoadPermissionsAsync()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
                {
                    // Check each permission using PermissionService
                    CanViewUsers = await _permissionService.HasPermissionAsync(userId, "USER.VIEW");
                    CanManageUsers = await _permissionService.HasPermissionAsync(userId, "USER.MANAGE");
                    CanCreateUsers = await _permissionService.HasPermissionAsync(userId, "USER.CREATE");
                    CanEditUsers = await _permissionService.HasPermissionAsync(userId, "USER.EDIT");
                    CanDeleteUsers = await _permissionService.HasPermissionAsync(userId, "USER.DELETE");

                    // Log permission checks
                    _logger.LogInfo("UsersInfo", $"User {userId} permissions: VIEW={CanViewUsers}, MANAGE={CanManageUsers}, CREATE={CanCreateUsers}, EDIT={CanEditUsers}, DELETE={CanDeleteUsers}");
                }
                else
                {
                    _logger.LogWarning("UsersInfo", "User ID not found in claims");
                    CanViewUsers = CanManageUsers = CanCreateUsers = CanEditUsers = CanDeleteUsers = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error loading permissions: {ex.Message}");
                CanViewUsers = CanManageUsers = CanCreateUsers = CanEditUsers = CanDeleteUsers = false;
            }
        }

        public async Task<bool> HasPermissionAsync(string permission)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    _logger.LogWarning("UsersInfo", "Permission check failed: User ID not found in claims");
                    return false;
                }

                var hasPermission = await _permissionService.HasPermissionAsync(userId, permission);
                _logger.LogInfo("UsersInfo", $"Permission check for {permission}: {hasPermission} (User: {userId})");
                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error checking permission {permission}: {ex.Message}");
                return false;
            }
        }

        public async Task<IActionResult> OnGetAsync(
            string searchTerm = "",
            string roleFilter = "",
            string statusFilter = "",
            string tab = "all",
            int pageNumber = 1)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
                {
                    _logger.LogWarning("UsersInfo", "Access attempt without valid user ID");
                    return RedirectToPage("/Error");
                }

                // Check base permission for viewing users
                if (!await HasPermissionAsync("USER.VIEW"))
                {
                    _logger.LogWarning("UsersInfo", $"Access denied: User {userId} attempted to access UsersInfo without USER.VIEW permission");
                    return RedirectToPage("/Error");
                }

                // Set permission flags
                CanViewUsers = true; // We already checked this above
                CanManageUsers = await HasPermissionAsync("USER.MANAGE");
                CanCreateUsers = await HasPermissionAsync("USER.CREATE");
                CanEditUsers = await HasPermissionAsync("USER.EDIT");
                CanDeleteUsers = await HasPermissionAsync("USER.DELETE");

                _logger.LogInfo("UsersInfo", $"User {userId} permissions loaded: VIEW={CanViewUsers}, MANAGE={CanManageUsers}, CREATE={CanCreateUsers}, EDIT={CanEditUsers}, DELETE={CanDeleteUsers}");

                Filters.SearchTerm = searchTerm;
                Filters.RoleFilter = roleFilter;
                Filters.StatusFilter = statusFilter;
                Filters.CurrentTab = tab;
                Pagination.CurrentPage = pageNumber < 1 ? 1 : pageNumber;

                await LoadRoleOptionsAsync();
                await LoadUsersAsync();

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error loading UsersInfo page: {ex.Message}");
                return RedirectToPage("/Error");
            }
        }

        public async Task<IActionResult> OnPostAdd()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            await LoadPermissionsAsync();
            if (!CanCreateUsers)
            {
                _logger.LogWarning("Access Denied", $"User {userId} attempted to create user without CREATE permission");
                return Forbid();
            }

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
                    "SELECT COUNT(1) FROM Users WHERE EMAIL = @Email",
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

                // Insert new user
                var query = @"
                    INSERT INTO Users (FULL_NAME, EMAIL, PASSWORD_HASH, ROLE_ID, DATE_REGISTERED, LAST_LOGIN) 
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

                _logger.LogInfo("UsersInfo", $"User {userId} created new user with email {UserInput.Email}");
                TempData["SuccessMessage"] = "User added successfully!";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error creating user: {ex.Message}");
                ModelState.AddModelError("", "An error occurred while creating the user.");
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostUpdate()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            await LoadPermissionsAsync();
            if (!CanEditUsers)
            {
                _logger.LogWarning("Access Denied", $"User {userId} attempted to update user without EDIT permission");
                return Forbid();
            }

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
                    "SELECT COUNT(1) FROM Users WHERE EMAIL = @Email AND USER_ID <> @UserId",
                    new { UserInput.Email, UserInput.UserId });

                if (emailExists)
                {
                    ModelState.AddModelError("UserInput.Email", "Email already registered by another user");
                    await LoadRoleOptionsAsync();
                    await LoadUsersAsync();
                    return Page();
                }

                if (string.IsNullOrEmpty(UserInput.Password))
                {
                    // Update without password
                    var query = @"
                        UPDATE Users SET 
                            FULL_NAME = @FullName, 
                            EMAIL = @Email, 
                            ROLE_ID = @RoleId,
                            LAST_LOGIN = CASE WHEN @IsActive = 1 THEN ISNULL(LAST_LOGIN, GETDATE()) ELSE NULL END
                        WHERE USER_ID = @UserId";

                    await connection.ExecuteAsync(query, UserInput);
                }
                else
                {
                    // Update with new password
                    var passwordHash = BCrypt.Net.BCrypt.HashPassword(UserInput.Password);
                    var query = @"
                        UPDATE Users SET 
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

                _logger.LogInfo("UsersInfo", $"User {userId} updated user {UserInput.UserId}");
                TempData["SuccessMessage"] = "User updated successfully!";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error updating user: {ex.Message}");
                ModelState.AddModelError("", "An error occurred while updating the user.");
                await LoadRoleOptionsAsync();
                await LoadUsersAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDelete(int userId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var currentUserId))
            {
                return Forbid();
            }

            await LoadPermissionsAsync();
            if (!CanDeleteUsers)
            {
                _logger.LogWarning("Access Denied", $"User {currentUserId} attempted to delete user {userId} without DELETE permission");
                return Forbid();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First check if user exists
                var userExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM Users WHERE USER_ID = @UserId",
                    new { UserId = userId });

                if (!userExists)
                {
                    return NotFound();
                }

                // Delete the user
                await connection.ExecuteAsync(
                    "DELETE FROM Users WHERE USER_ID = @UserId",
                    new { UserId = userId });

                _logger.LogInfo("UsersInfo", $"User {currentUserId} deleted user {userId}");
                TempData["SuccessMessage"] = "User deleted successfully!";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error deleting user: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while deleting the user.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(int userId, bool currentStatus)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var currentUserId))
            {
                return Forbid();
            }

            await LoadPermissionsAsync();
            if (!CanEditUsers)
            {
                _logger.LogWarning("Access Denied", $"User {currentUserId} attempted to toggle status for user {userId} without EDIT permission");
                return Forbid();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First check if user exists
                var userExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM Users WHERE USER_ID = @UserId",
                    new { UserId = userId });

                if (!userExists)
                {
                    return NotFound();
                }

                // Update the user's status
                var newStatus = !currentStatus;
                var result = await connection.ExecuteAsync(
                    "UPDATE Users SET IS_ACTIVE = @NewStatus WHERE USER_ID = @UserId",
                    new { NewStatus = newStatus, UserId = userId });

                if (result > 0)
                {
                    _logger.LogInfo("UsersInfo", $"User {currentUserId} toggled status for user {userId} to {(newStatus ? "active" : "inactive")}");
                    TempData["SuccessMessage"] = $"User status has been {(newStatus ? "activated" : "deactivated")} successfully.";
                    
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new JsonResult(new { success = true, newStatus = newStatus });
                    }
                }
                else
                {
                    _logger.LogWarning("UsersInfo", $"Failed to update status for user {userId}");
                    TempData["ErrorMessage"] = "Failed to update user status.";
                    
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new JsonResult(new { success = false, message = "Failed to update user status." });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error toggling status for user {userId}: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while updating user status.";
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = false, message = "An error occurred while updating user status." });
                }
            }

            return RedirectToPage();
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
                    FROM Users
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

            var offset = (Pagination.CurrentPage - 1) * Pagination.PageSize;
            string selectQuery;

            if (Filters.CurrentTab == "audit")
            {
                // Get audit logs
                var auditQuery = @"
                    SELECT 
                        pal.CHANGE_DATE as ChangeDate,
                        u.FULL_NAME as UserName,
                        p.PERMISSION_NAME as PermissionName,
                        pal.ACTION as Action,
                        ISNULL(cb.FULL_NAME, 'System') as ChangedByName,
                        pal.REASON as Reason
                    FROM PERMISSION_AUDIT_LOG pal
                    JOIN USERS u ON pal.USER_ID = u.USER_ID
                    JOIN PERMISSIONS p ON pal.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN USERS cb ON pal.CHANGED_BY = cb.USER_ID
                    ORDER BY pal.CHANGE_DATE DESC
                    OFFSET @Offset ROWS
                    FETCH NEXT @PageSize ROWS ONLY";

                try
                {
                    AuditLogs = (await connection.QueryAsync<AuditLog>(
                        auditQuery, 
                        new { 
                            Offset = (Pagination.CurrentPage - 1) * Pagination.PageSize,
                            PageSize = Pagination.PageSize
                        })).ToList();

                    // Get total count for pagination
                    Pagination.TotalItems = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PERMISSION_AUDIT_LOG");
                }
                catch (Exception ex)
                {
                    _logger.LogError("UsersInfo", $"Error loading audit logs: {ex.Message}");
                    TempData["ErrorMessage"] = "Failed to load audit logs. Please try again later.";
                }

                return;
            }

            // Build where clause for users list
            var whereClauses = new List<string> { "1=1" };
            var parameters = new DynamicParameters();
            parameters.Add("Offset", offset);
            parameters.Add("PageSize", Pagination.PageSize);

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
            Pagination.TotalItems = await connection.ExecuteScalarAsync<int>($@"
                SELECT COUNT(*)
                FROM Users u
                INNER JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                WHERE {whereClause}", parameters);

            // Get paginated users
            selectQuery = $@"
                SELECT
                    u.USER_ID as UserId,
                    u.FULL_NAME as FullName,
                    u.EMAIL as Email,
                    r.ROLE_NAME as RoleName,
                    u.IS_ACTIVE as IsActive,
                    u.DATE_REGISTERED as DateRegistered
                FROM Users u
                INNER JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                WHERE {whereClause}
                ORDER BY u.DATE_REGISTERED DESC
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            Users = (await connection.QueryAsync<UserViewModel>(selectQuery, parameters)).ToList();
        }

        private async Task LoadRoleOptionsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT ROLE_ID as Value, ROLE_NAME as Text FROM ROLES ORDER BY ROLE_NAME";
            var roles = (await connection.QueryAsync<RoleOption>(query)).AsList();

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

        public async Task<IActionResult> OnGetLoadPermissionsAsync(int userId)
        {
            try
            {
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return new JsonResult(new { error = "Not authenticated" });
                }

                if (!await _permissionService.HasPermissionAsync(int.Parse(currentUserId), "USER.MANAGE"))
                {
                    return new JsonResult(new { error = "Access denied" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get current permissions
                var currentPermissionsQuery = @"
                    SELECT 
                        p.PERMISSION_ID as PermissionId,
                        p.PERMISSION_NAME as PermissionName,
                        ISNULL(pc.CATEGORY_NAME, 'General') as CategoryName,
                        up.ASSIGNED_DATE as AssignedDate,
                        ISNULL(u.FULL_NAME, 'System') as AssignedByName
                    FROM USER_PERMISSIONS up
                    JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                    LEFT JOIN PERMISSION_CATEGORIES pc ON p.CATEGORY_ID = pc.CATEGORY_ID
                    LEFT JOIN USERS u ON up.ASSIGNED_BY = u.USER_ID
                    WHERE up.USER_ID = @UserId AND up.IS_GRANT = 1
                    ORDER BY pc.CATEGORY_NAME, p.PERMISSION_NAME";

                var currentPermissions = await connection.QueryAsync<UserPermissionViewModel>(
                    currentPermissionsQuery, new { UserId = userId });

                // Get available permissions
                var availablePermissionsQuery = @"
                    SELECT 
                        p.PERMISSION_ID as PermissionId,
                        p.PERMISSION_NAME as PermissionName,
                        p.DESCRIPTION as Description
                    FROM PERMISSIONS p
                    WHERE NOT EXISTS (
                        SELECT 1 FROM USER_PERMISSIONS up 
                        WHERE up.PERMISSION_ID = p.PERMISSION_ID 
                        AND up.USER_ID = @UserId
                        AND up.IS_GRANT = 1
                        AND (up.EXPIRATION_DATE IS NULL OR up.EXPIRATION_DATE > GETDATE())
                    )
                    ORDER BY p.PERMISSION_NAME";

                var availablePermissions = await connection.QueryAsync<PermissionViewModel>(
                    availablePermissionsQuery, new { UserId = userId });

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

                var permissionHistory = await connection.QueryAsync<PermissionHistoryEntry>(
                    historyQuery, new { UserId = userId });

                return new JsonResult(new
                {
                    currentPermissions = currentPermissions,
                    availablePermissions = availablePermissions,
                    permissionHistory = permissionHistory
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error loading permissions: {ex.Message}");
                return new JsonResult(new { error = "An error occurred while loading permissions." });
            }
        }

        public async Task<IActionResult> OnPostAssignPermissionAsync(int userId, int permissionId, string reason)
        {
            try
            {
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    _logger.LogWarning("UsersInfo", "Permission assignment attempted without authentication");
                    return new JsonResult(new { success = false, error = "Not authenticated" });
                }

                if (!await HasPermissionAsync("USER.MANAGE"))
                {
                    _logger.LogWarning("UsersInfo", $"User {currentUserId} attempted to assign permission without USER.MANAGE permission");
                    return new JsonResult(new { success = false, error = "Access denied" });
                }

                // Validate inputs
                if (userId <= 0 || permissionId <= 0)
                {
                    _logger.LogWarning("UsersInfo", $"Invalid input parameters: userId={userId}, permissionId={permissionId}");
                    return new JsonResult(new { success = false, error = "Invalid input parameters" });
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return new JsonResult(new { success = false, error = "Reason is required" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First verify the user exists
                var userExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM Users WHERE USER_ID = @UserId",
                    new { UserId = userId });

                if (!userExists)
                {
                    _logger.LogWarning("UsersInfo", $"Attempted to assign permission to non-existent user: {userId}");
                    return new JsonResult(new { success = false, error = "User not found" });
                }

                // Verify the permission exists
                var permissionExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM PERMISSIONS WHERE PERMISSION_ID = @PermissionId",
                    new { PermissionId = permissionId });

                if (!permissionExists)
                {
                    _logger.LogWarning("UsersInfo", $"Attempted to assign non-existent permission: {permissionId}");
                    return new JsonResult(new { success = false, error = "Permission not found" });
                }

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // Check if permission already exists and is active
                    var existingPermission = await connection.QueryFirstOrDefaultAsync<dynamic>(
                        @"SELECT IS_GRANT, EXPIRATION_DATE 
                          FROM USER_PERMISSIONS 
                          WHERE USER_ID = @UserId 
                          AND PERMISSION_ID = @PermissionId",
                        new { UserId = userId, PermissionId = permissionId },
                        transaction);

                    if (existingPermission != null && existingPermission.IS_GRANT == true && 
                        (existingPermission.EXPIRATION_DATE == null || existingPermission.EXPIRATION_DATE > DateTime.Now))
                    {
                        return new JsonResult(new { success = false, error = "This permission is already assigned to the user." });
                    }

                    // Add or update permission
                    await connection.ExecuteAsync(@"
                        MERGE INTO USER_PERMISSIONS AS target
                        USING (SELECT @UserId as USER_ID, @PermissionId as PERMISSION_ID) AS source
                        ON (target.USER_ID = source.USER_ID AND target.PERMISSION_ID = source.PERMISSION_ID)
                        WHEN MATCHED THEN
                            UPDATE SET 
                                IS_GRANT = 1,
                                ASSIGNED_BY = @AssignedBy,
                                ASSIGNED_DATE = GETDATE(),
                                EXPIRATION_DATE = NULL
                        WHEN NOT MATCHED THEN
                            INSERT (USER_ID, PERMISSION_ID, IS_GRANT, ASSIGNED_BY, ASSIGNED_DATE)
                            VALUES (@UserId, @PermissionId, 1, @AssignedBy, GETDATE());",
                        new { 
                            UserId = userId,
                            PermissionId = permissionId,
                            AssignedBy = int.Parse(currentUserId)
                        },
                        transaction);

                    // Log the change in audit log
                    await connection.ExecuteAsync(@"
                        INSERT INTO PERMISSION_AUDIT_LOG 
                        (USER_ID, PERMISSION_ID, ACTION, CHANGED_BY, CHANGE_DATE, REASON)
                        VALUES 
                        (@UserId, @PermissionId, 'GRANT', @ChangedBy, GETDATE(), @Reason)",
                        new
                        {
                            UserId = userId,
                            PermissionId = permissionId,
                            ChangedBy = int.Parse(currentUserId),
                            Reason = reason
                        },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("UsersInfo", $"User {currentUserId} successfully assigned permission {permissionId} to user {userId}");
                    
                    return new JsonResult(new { 
                        success = true, 
                        message = "Permission assigned successfully!"
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("UsersInfo", $"Database error while assigning permission: {ex.Message}");
                    throw; // Let the outer catch handle it
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error assigning permission: {ex.Message}");
                return new JsonResult(new { 
                    success = false, 
                    error = "An error occurred while assigning the permission. Please try again later." 
                });
            }
        }

        public async Task<IActionResult> OnPostRevokePermissionAsync(int userId, int permissionId)
        {
            try
            {
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    _logger.LogWarning("UsersInfo", "Permission revocation attempted without authentication");
                    return new JsonResult(new { success = false, error = "Not authenticated" });
                }

                if (!await HasPermissionAsync("USER.MANAGE"))
                {
                    _logger.LogWarning("UsersInfo", $"User {currentUserId} attempted to revoke permission without USER.MANAGE permission");
                    return new JsonResult(new { success = false, error = "Access denied" });
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    // First check if the permission exists and is granted
                    var permissionExists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM USER_PERMISSIONS WHERE USER_ID = @UserId AND PERMISSION_ID = @PermissionId AND IS_GRANT = 1",
                        new { UserId = userId, PermissionId = permissionId },
                        transaction);

                    if (!permissionExists)
                    {
                        return new JsonResult(new { success = false, error = "Permission not found or already revoked." });
                    }

                    // Update permission to revoked state
                    await connection.ExecuteAsync(@"
                        UPDATE USER_PERMISSIONS 
                        SET IS_GRANT = 0,
                            ASSIGNED_BY = @AssignedBy,
                            ASSIGNED_DATE = GETDATE()
                        WHERE USER_ID = @UserId 
                        AND PERMISSION_ID = @PermissionId",
                        new { 
                            UserId = userId, 
                            PermissionId = permissionId,
                            AssignedBy = int.Parse(currentUserId)
                        },
                        transaction);

                    // Log the change in audit log
                    await connection.ExecuteAsync(@"
                        INSERT INTO PERMISSION_AUDIT_LOG 
                        (USER_ID, PERMISSION_ID, ACTION, CHANGED_BY, CHANGE_DATE, REASON)
                        VALUES 
                        (@UserId, @PermissionId, 'REVOKE', @ChangedBy, GETDATE(), 'Permission revoked by administrator')",
                        new
                        {
                            UserId = userId,
                            PermissionId = permissionId,
                            ChangedBy = int.Parse(currentUserId)
                        },
                        transaction);

                    await transaction.CommitAsync();
                    _logger.LogInfo("UsersInfo", $"User {currentUserId} revoked permission {permissionId} from user {userId}");
                    return new JsonResult(new { success = true });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError("UsersInfo", $"Database error while revoking permission: {ex.Message}");
                    return new JsonResult(new { success = false, error = "An error occurred while revoking the permission." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("UsersInfo", $"Error revoking permission: {ex.Message}");
                return new JsonResult(new { success = false, error = "An error occurred while processing your request." });
            }
        }
    }
}