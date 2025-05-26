using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using E_Learning_Platform.Hubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using E_Learning_Platform.Services;
using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Pages
{
    [Authorize] // Only requires authentication, no specific permissions
    public class AdminDashboardModel : PageModel
    {
        //private readonly IConfiguration _configuration;

        private readonly string _connectionString;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly ILoggingService _logger;

        public AdminDashboardModel(IHubContext<DashboardHub> hubContext, ILoggingService logger)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
            _hubContext = hubContext;
            TopCourses = new List<CoursePerformance>();
            UserStatistics = new UserStats();
            UserRoles = new List<UserRoleInfo>();
            RecentUsers = new List<RecentUser>();
            UserActivityData = new List<UserActivity>();
            _logger = logger;
        }

        public int TotalCourses { get; set; }
        public int ActiveStudents { get; set; }
        public int CertificatesIssued { get; set; }

        // Previous month stats for trend calculation
        public int PreviousMonthCourses { get; set; }
        public int PreviousMonthStudents { get; set; }
        public int PreviousMonthCertificates { get; set; }

        public List<CoursePerformance> TopCourses { get; set; }
        public UserStats UserStatistics { get; set; }
        public List<UserRoleInfo> UserRoles { get; set; }
        public List<RecentUser> RecentUsers { get; set; }
        public List<UserActivity> UserActivityData { get; set; }

        public class UserStats
        {
            public int TotalUsers { get; set; }
            public int ActiveUsers { get; set; }
            public int NewUsersToday { get; set; }
            public int MfaEnabledUsers { get; set; }
        }

        public class UserRoleInfo
        {
            public required string RoleName { get; set; }
            public int UserCount { get; set; }
            public decimal Percentage { get; set; }
        }

        public class RecentUser
        {
            public required string FullName { get; set; }
            public required string Email { get; set; }
            public required string Username { get; set; }
            public DateTime DateRegistered { get; set; }
            public bool IsActive { get; set; }
        }

        public class UserActivity
        {
            public required string Date { get; set; }
            public int LoginCount { get; set; }
        }

        public class CoursePerformance
        {
            public required string CourseName { get; set; }
            public int StudentCount { get; set; }
            public decimal CompletionRate { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                // No permission checks needed here
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // Get current month stats
                    TotalCourses = await GetTotalCoursesAsync(connection);
                    ActiveStudents = await GetActiveStudentsAsync(connection);
                    CertificatesIssued = await GetCertificatesIssuedAsync(connection);

                    // Get previous month stats for trend calculation
                    PreviousMonthCourses = await GetTotalCoursesAsync(connection, -1);
                    PreviousMonthStudents = await GetActiveStudentsAsync(connection, -1);
                    PreviousMonthCertificates = await GetCertificatesIssuedAsync(connection, -1);

                    // Get user statistics
                    await GetUserStatisticsAsync(connection);
                    await GetUserRolesAsync(connection);
                    await GetRecentUsersAsync(connection);
                    await GetUserActivityDataAsync(connection);

                    // Get top performing courses
                    TopCourses = await GetTopCoursesAsync(connection);

                    // Send initial real-time updates
                    await SendInitialRealTimeData();
                }
                return Page();
            }
            catch (System.Exception ex)
            {
                _logger.LogError("AdminDashboard", $"Error loading dashboard: {ex.Message}");
                return RedirectToPage("/Error");
            }
        }

        private async Task GetUserStatisticsAsync(SqlConnection connection)
        {
            string query = @"
                SELECT 
                    COUNT(*) as TotalUsers,
                    SUM(CASE WHEN IS_ACTIVE = 1 THEN 1 ELSE 0 END) as ActiveUsers,
                    SUM(CASE WHEN CAST(DATE_REGISTERED AS DATE) = CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END) as NewUsersToday,
                    SUM(CASE WHEN MFA_ENABLED = 1 THEN 1 ELSE 0 END) as MfaEnabledUsers
                FROM USERS";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        UserStatistics = new UserStats
                        {
                            TotalUsers = reader.GetInt32(0),
                            ActiveUsers = reader.GetInt32(1),
                            NewUsersToday = reader.GetInt32(2),
                            MfaEnabledUsers = reader.GetInt32(3)
                        };
                    }
                }
            }
        }

        private async Task GetUserRolesAsync(SqlConnection connection)
        {
            string query = @"
                SELECT 
                    r.ROLE_NAME,
                    COUNT(u.USER_ID) as UserCount,
                    CAST(COUNT(u.USER_ID) * 100.0 / (SELECT COUNT(*) FROM USERS) AS DECIMAL(5,2)) as Percentage
                FROM ROLES r
                LEFT JOIN USERS u ON r.ROLE_ID = u.ROLE_ID
                GROUP BY r.ROLE_NAME";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        UserRoles.Add(new UserRoleInfo
                        {
                            RoleName = reader.GetString(0),
                            UserCount = reader.GetInt32(1),
                            Percentage = reader.GetDecimal(2)
                        });
                    }
                }
            }
        }

        private async Task GetRecentUsersAsync(SqlConnection connection)
        {
            string query = @"
                SELECT TOP 10
                    ISNULL(FULL_NAME, 'N/A') as FULL_NAME,
                    ISNULL(EMAIL, 'N/A') as EMAIL,
                    ISNULL(USERNAME, 'N/A') as USERNAME,
                    DATE_REGISTERED,
                    IS_ACTIVE
                FROM USERS
                ORDER BY DATE_REGISTERED DESC";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        RecentUsers.Add(new RecentUser
                        {
                            FullName = reader.GetString(0),
                            Email = reader.GetString(1),
                            Username = reader.GetString(2),
                            DateRegistered = reader.GetDateTime(3),
                            IsActive = reader.GetBoolean(4)
                        });
                    }
                }
            }
        }

        private async Task GetUserActivityDataAsync(SqlConnection connection)
        {
            string query = @"
                SELECT 
                    CAST(LAST_LOGIN AS DATE) as LoginDate,
                    COUNT(*) as LoginCount
                FROM USERS
                WHERE LAST_LOGIN >= DATEADD(day, -30, GETDATE())
                GROUP BY CAST(LAST_LOGIN AS DATE)
                ORDER BY LoginDate";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        UserActivityData.Add(new UserActivity
                        {
                            Date = reader.GetDateTime(0).ToString("MM/dd"),
                            LoginCount = reader.GetInt32(1)
                        });
                    }
                }
            }
        }

        private async Task SendInitialRealTimeData()
        {
            if (_hubContext != null)
            {
                // Send active users count
                await _hubContext.Clients.All.SendAsync("UpdateActiveUsers", UserStatistics.ActiveUsers);

                // Send role distribution data
                foreach (var role in UserRoles)
                {
                    await _hubContext.Clients.All.SendAsync("UpdateRoleDistribution", role.RoleName, role.UserCount);
                }

                // Send user activity data
                foreach (var activity in UserActivityData)
                {
                    await _hubContext.Clients.All.SendAsync("UpdateUserActivity", activity.Date, activity.LoginCount);
                }

                // Send course progress for top courses
                foreach (var course in TopCourses)
                {
                    await _hubContext.Clients.All.SendAsync("CourseProgressUpdated", course.CourseName, course.CompletionRate);
                }

                // Send initial notification
                await _hubContext.Clients.All.SendAsync("ReceiveNotification", 
                    "Dashboard initialized with real-time data", "success");
            }
        }

        private async Task<int> GetTotalCoursesAsync(SqlConnection connection, int monthsOffset = 0)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM COURSES 
                WHERE CREATION_DATE <= DATEADD(month, @MonthOffset, GETDATE())";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@MonthOffset", monthsOffset);
                return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            }
        }

        private async Task<int> GetActiveStudentsAsync(SqlConnection connection, int monthsOffset = 0)
        {
            string query = @"
                SELECT COUNT(DISTINCT USER_ID) 
                FROM COURSE_ENROLLMENTS 
                WHERE STATUS = 'Active' AND 
                      ENROLLMENT_DATE <= DATEADD(month, @MonthOffset, GETDATE())";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@MonthOffset", monthsOffset);
                return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            }
        }

        private async Task<int> GetCertificatesIssuedAsync(SqlConnection connection, int monthsOffset = 0)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM COURSE_ENROLLMENTS 
                WHERE STATUS = 'Completed' AND 
                      COMPLETION_DATE IS NOT NULL AND
                      COMPLETION_DATE <= DATEADD(month, @MonthOffset, GETDATE()) AND
                      COMPLETION_DATE >= DATEADD(month, @MonthOffset-1, GETDATE())";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@MonthOffset", monthsOffset);
                return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
            }
        }

        private async Task<List<CoursePerformance>> GetTopCoursesAsync(SqlConnection connection)
        {
            List<CoursePerformance> courses = new List<CoursePerformance>();

            string query = @"
                SELECT 
                    c.TITLE,
                    COUNT(e.USER_ID) AS StudentCount,
                    (SUM(CASE WHEN e.STATUS = 'Completed' THEN 1 ELSE 0 END) * 100.0 / COUNT(e.USER_ID)) AS CompletionRate
                FROM 
                    COURSES c
                JOIN 
                    COURSE_ENROLLMENTS e ON c.COURSE_ID = e.COURSE_ID
                GROUP BY 
                    c.TITLE
                ORDER BY 
                    StudentCount DESC, CompletionRate DESC
                OFFSET 0 ROWS
                FETCH NEXT 4 ROWS ONLY";

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        courses.Add(new CoursePerformance
                        {
                            CourseName = reader.GetString(0),
                            StudentCount = reader.GetInt32(1),
                            CompletionRate = reader.GetDecimal(2)
                        });
                    }
                }
            }

            return courses;
        }

        public string CalculateTrendPercentage(int current, int previous)
        {
            if (previous == 0)
                return "100";

            decimal percentChange = ((decimal)current - previous) / previous * 100;
            return Math.Round(percentChange).ToString();
        }

        public string GetTrendClass(int current, int previous)
        {
            return current >= previous ? "" : "down";
        }

        public string GetCompletionClass(decimal completionRate)
        {
            if (completionRate >= 90)
                return "completion-high";
            else if (completionRate >= 75)
                return "completion-medium";
            else
                return "completion-low";
        }
    }
}