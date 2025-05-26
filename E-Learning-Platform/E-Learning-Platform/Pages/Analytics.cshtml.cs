using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages
{
    [Authorize]
    public class AnalyticsModel : PageModel
    {
        private readonly ILogger<AnalyticsModel> _logger;
        private readonly string _connectionString;

        public int TotalUsers { get; set; }
        public int ActiveCourses { get; set; }
        public decimal CompletionRate { get; set; }
        public int ActiveEnrollments { get; set; }

        public AnalyticsModel(ILogger<AnalyticsModel> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                _logger.LogInformation("Analytics page accessed");
                await LoadQuickStatistics();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading Analytics page: {ex.Message}");
                TempData["ErrorMessage"] = "Error loading analytics data. Please try again later.";
                return RedirectToPage("/Error");
            }
        }

        private async Task LoadQuickStatistics()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get total active users
                TotalUsers = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM USERS WHERE IS_ACTIVE = 1");

                // Get active courses (assuming courses also have IS_ACTIVE)
                ActiveCourses = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM COURSES WHERE IS_ACTIVE = 1");

                // Get completion rate from COURSE_ENROLLMENTS
                var completionStats = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                    SELECT 
                        SUM(CASE WHEN STATUS = 'Completed' THEN 1 ELSE 0 END) as completed,
                        COUNT(*) as total
                    FROM COURSE_ENROLLMENTS");
                CompletionRate = completionStats.total > 0 ? 
                    (decimal)completionStats.completed / completionStats.total * 100 : 0;

                // Get active enrollments
                ActiveEnrollments = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE STATUS = 'ACTIVE'");

                _logger.LogInformation("Quick statistics loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading quick statistics: {ex.Message}");
                throw;
            }
        }
    }
} 