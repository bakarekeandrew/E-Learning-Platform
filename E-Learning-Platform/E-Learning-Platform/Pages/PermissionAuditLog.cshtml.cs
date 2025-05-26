using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using E_Learning_Platform.Services;
using System.ComponentModel.DataAnnotations;

namespace E_Learning_Platform.Pages
{
    [Authorize(Policy = "USER.MANAGE")]
    public class PermissionAuditLogModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<PermissionAuditLogModel> _logger;

        public PermissionAuditLogModel(IConfiguration configuration, ILogger<PermissionAuditLogModel> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public List<AuditLogEntry> AuditLogs { get; set; } = new();
        public List<SelectListItem> UsersList { get; set; } = new();
        public AuditLogFilters Filters { get; set; } = new();

        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);

        public class AuditLogEntry
        {
            public int LogId { get; set; }
            public string UserName { get; set; }
            public string PermissionName { get; set; }
            public string Action { get; set; }
            public string ChangedByName { get; set; }
            public DateTime ChangeDate { get; set; }
            public string Reason { get; set; }
        }

        public class AuditLogFilters
        {
            public int? UserId { get; set; }
            public string Action { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int? userId, string action, DateTime? startDate, DateTime? endDate, int pageNumber = 1)
        {
            var currentUserIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (currentUserIdClaim == null || !int.TryParse(currentUserIdClaim.Value, out var currentUserId))
            {
                return RedirectToPage("/Login");
            }

            // Only user ID 14 or users with PERMISSION.MANAGE can access this page
            if (currentUserId != 14 && !await HasPermissionAsync(currentUserId, "PERMISSION.MANAGE"))
            {
                return Forbid();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Set filters
                Filters = new AuditLogFilters
                {
                    UserId = userId,
                    Action = action,
                    StartDate = startDate,
                    EndDate = endDate
                };

                CurrentPage = pageNumber;

                // Get users for dropdown
                var usersQuery = @"
                    SELECT 
                        USER_ID as Value,
                        FULL_NAME as Text
                    FROM AppUsers
                    ORDER BY FULL_NAME";

                UsersList = (await connection.QueryAsync<SelectListItem>(usersQuery)).ToList();

                // Build audit log query
                var whereClause = new List<string>();
                var parameters = new DynamicParameters();

                if (userId.HasValue)
                {
                    whereClause.Add("pal.USER_ID = @UserId");
                    parameters.Add("UserId", userId.Value);
                }

                if (!string.IsNullOrEmpty(action))
                {
                    whereClause.Add("pal.ACTION = @Action");
                    parameters.Add("Action", action);
                }

                if (startDate.HasValue)
                {
                    whereClause.Add("pal.CHANGE_DATE >= @StartDate");
                    parameters.Add("StartDate", startDate.Value);
                }

                if (endDate.HasValue)
                {
                    whereClause.Add("pal.CHANGE_DATE <= @EndDate");
                    parameters.Add("EndDate", endDate.Value.AddDays(1));
                }

                var whereClauseString = whereClause.Any() ? $"WHERE {string.Join(" AND ", whereClause)}" : "";

                // Get total count
                var countQuery = $@"
                    SELECT COUNT(*)
                    FROM PERMISSION_AUDIT_LOG pal
                    {whereClauseString}";

                TotalItems = await connection.ExecuteScalarAsync<int>(countQuery, parameters);

                // Get audit logs
                var auditQuery = $@"
                    SELECT 
                        pal.LOG_ID as LogId,
                        u.FULL_NAME as UserName,
                        p.PERMISSION_NAME as PermissionName,
                        pal.ACTION as Action,
                        cb.FULL_NAME as ChangedByName,
                        pal.CHANGE_DATE as ChangeDate,
                        pal.REASON as Reason
                    FROM PERMISSION_AUDIT_LOG pal
                    JOIN USERS u ON pal.USER_ID = u.USER_ID
                    JOIN PERMISSIONS p ON pal.PERMISSION_ID = p.PERMISSION_ID
                    JOIN USERS cb ON pal.CHANGED_BY = cb.USER_ID
                    {whereClauseString}
                    ORDER BY pal.CHANGE_DATE DESC
                    OFFSET @Offset ROWS
                    FETCH NEXT @PageSize ROWS ONLY";

                parameters.Add("Offset", (CurrentPage - 1) * PageSize);
                parameters.Add("PageSize", PageSize);

                AuditLogs = (await connection.QueryAsync<AuditLogEntry>(auditQuery, parameters)).ToList();

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving permission audit log");
                TempData["ErrorMessage"] = "An error occurred while retrieving the audit log.";
                return RedirectToPage("/UsersInfo");
            }
        }

        private async Task<bool> HasPermissionAsync(int userId, string permission)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT COUNT(1)
                FROM USER_PERMISSIONS up
                JOIN PERMISSIONS p ON up.PERMISSION_ID = p.PERMISSION_ID
                WHERE up.USER_ID = @UserId 
                AND p.PERMISSION_NAME = @Permission
                AND up.IS_GRANT = 1";

            var count = await connection.ExecuteScalarAsync<int>(query, new { UserId = userId, Permission = permission });
            return count > 0;
        }

        public string GetPageUrl(int pageNumber)
        {
            var queryParams = new List<string>
            {
                $"pageNumber={pageNumber}"
            };

            if (Filters.UserId.HasValue)
                queryParams.Add($"userId={Filters.UserId}");

            if (!string.IsNullOrEmpty(Filters.Action))
                queryParams.Add($"action={Filters.Action}");

            if (Filters.StartDate.HasValue)
                queryParams.Add($"startDate={Filters.StartDate.Value:yyyy-MM-dd}");

            if (Filters.EndDate.HasValue)
                queryParams.Add($"endDate={Filters.EndDate.Value:yyyy-MM-dd}");

            return $"?{string.Join("&", queryParams)}";
        }
    }
} 