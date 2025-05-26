using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Analytics
{
    [Authorize]
    public class EngagementModel : PageModel
    {
        private readonly ILogger<EngagementModel> _logger;
        private readonly string _connectionString;

        public int ActiveUsers { get; set; }
        public int AverageSessionDuration { get; set; }
        public decimal CourseCompletionRate { get; set; }
        public int NewUsers { get; set; }

        public EngagementModel(ILogger<EngagementModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get active users in last 30 days (users who accessed courses)
                ActiveUsers = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(DISTINCT cp.USER_ID) 
                    FROM COURSE_PROGRESS cp
                    JOIN USERS u ON cp.USER_ID = u.USER_ID
                    WHERE cp.LAST_ACCESSED >= DATEADD(day, -30, GETDATE())
                    AND u.IS_ACTIVE = 1");

                // Get average session duration in minutes (estimated from course progress)
                AverageSessionDuration = await connection.ExecuteScalarAsync<int>(@"
                    WITH SessionDurations AS (
                        SELECT 
                            USER_ID,
                            DATEDIFF(minute, 
                                LAG(LAST_ACCESSED) OVER (PARTITION BY USER_ID ORDER BY LAST_ACCESSED), 
                                LAST_ACCESSED) as Duration
                        FROM COURSE_PROGRESS
                        WHERE LAST_ACCESSED >= DATEADD(day, -30, GETDATE())
                    )
                    SELECT ISNULL(AVG(CASE 
                        WHEN Duration > 0 AND Duration <= 240 THEN Duration -- Cap at 4 hours
                        ELSE 30 -- Default to 30 minutes for outliers
                    END), 30)
                    FROM SessionDurations
                    WHERE Duration IS NOT NULL");

                // Get course completion rate
                var completionStats = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                    SELECT 
                        SUM(CASE WHEN STATUS = 'Completed' THEN 1 ELSE 0 END) as completed,
                        COUNT(*) as total
                    FROM COURSE_ENROLLMENTS ce
                    JOIN USERS u ON ce.USER_ID = u.USER_ID
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    WHERE u.IS_ACTIVE = 1 AND c.IS_ACTIVE = 1");
                CourseCompletionRate = completionStats.total > 0 ? 
                    (decimal)completionStats.completed / completionStats.total : 0;

                // Get new users this month
                NewUsers = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) 
                    FROM USERS 
                    WHERE DATEPART(month, DATE_REGISTERED) = DATEPART(month, GETDATE())
                    AND DATEPART(year, DATE_REGISTERED) = DATEPART(year, GETDATE())
                    AND IS_ACTIVE = 1");

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading engagement analytics: {ex.Message}");
                TempData["ErrorMessage"] = "Error loading engagement data. Please try again later.";
                return RedirectToPage("/Error");
            }
        }
    }
} 