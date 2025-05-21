using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
using Dapper;
using System.Collections.Generic;

namespace E_Learning_Platform.Pages
{
    public class UserEngagementModel : PageModel
    {
        private readonly string _connectionString;

        public UserEngagementModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        // User Engagement Metrics
        public int DailyActiveUsers { get; set; }
        public int WeeklyActiveUsers { get; set; }
        public int MonthlyActiveUsers { get; set; }
        public double AverageSessionDuration { get; set; }
        public double RetentionRate { get; set; }
        public int NewUsersToday { get; set; }
        public int NewUsersThisWeek { get; set; }
        public int NewUsersThisMonth { get; set; }
        public double AverageTimeSpentPerCourse { get; set; }
        public int MostActiveTimeSlot { get; set; }
        public List<string> DailyLabels { get; set; } = new List<string>();
        public List<int> DailyActiveUsersData { get; set; } = new List<int>();

        public async Task OnGetAsync()
        {
            try
            {
                await LoadUserEngagementData();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : ""));
            }
        }

        private async Task LoadUserEngagementData()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            // Daily Active Users: users with enrollments today
            DailyActiveUsers = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT USER_ID) FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) = @Date",
                new { Date = today });

            // Weekly Active Users: users with enrollments this week
            WeeklyActiveUsers = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT USER_ID) FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });

            // Monthly Active Users: users with enrollments this month
            MonthlyActiveUsers = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT USER_ID) FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) >= @MonthStart",
                new { MonthStart = monthStart });

            // New Users Today/Week/Month
            NewUsersToday = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM USERS WHERE CAST(DATE_REGISTERED AS DATE) = @Date",
                new { Date = today });
            NewUsersThisWeek = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM USERS WHERE CAST(DATE_REGISTERED AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });
            NewUsersThisMonth = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM USERS WHERE CAST(DATE_REGISTERED AS DATE) >= @MonthStart",
                new { MonthStart = monthStart });

            // Retention Rate: percent of users who enrolled this week and also last week
            var lastWeekStart = weekStart.AddDays(-7);
            var usersLastWeek = await connection.QueryAsync<int>(
                @"SELECT DISTINCT USER_ID FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) >= @LastWeekStart AND CAST(ENROLLMENT_DATE AS DATE) < @WeekStart",
                new { LastWeekStart = lastWeekStart, WeekStart = weekStart });
            var usersThisWeek = await connection.QueryAsync<int>(
                @"SELECT DISTINCT USER_ID FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });
            var retained = usersThisWeek.Intersect(usersLastWeek).Count();
            RetentionRate = usersLastWeek.Count() > 0 ? (double)retained / usersLastWeek.Count() * 100 : 0;

            // Daily Active Users for the last 7 days (for chart)
            DailyLabels = new List<string>();
            DailyActiveUsersData = new List<int>();
            for (int i = 6; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                DailyLabels.Add(date.ToString("MMM dd"));
                var count = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(DISTINCT USER_ID) FROM COURSE_ENROLLMENTS WHERE CAST(ENROLLMENT_DATE AS DATE) = @Date",
                    new { Date = date });
                DailyActiveUsersData.Add(count);
            }

            // The following metrics require additional tables (UserSessions, UserActivities) which may not exist in your schema.
            // We'll set them to 0 for now, or you can provide the schema if you want them implemented.
            AverageSessionDuration = 0;
            AverageTimeSpentPerCourse = 0;
            MostActiveTimeSlot = 0;
        }

        #region Database Queries
        private async Task<int> GetDailyActiveUsersAsync(SqlConnection connection, DateTime date)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT UserId) 
                  FROM UserActivities 
                  WHERE CAST(ActivityDate AS DATE) = @Date",
                new { Date = date });
        }

        private async Task<int> GetWeeklyActiveUsersAsync(SqlConnection connection, DateTime weekStart)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT UserId) 
                  FROM UserActivities 
                  WHERE CAST(ActivityDate AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });
        }

        private async Task<int> GetMonthlyActiveUsersAsync(SqlConnection connection, DateTime monthStart)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT UserId) 
                  FROM UserActivities 
                  WHERE CAST(ActivityDate AS DATE) >= @MonthStart",
                new { MonthStart = monthStart });
        }

        private async Task<double> GetAverageSessionDurationAsync(SqlConnection connection, DateTime date)
        {
            return await connection.ExecuteScalarAsync<double>(
                @"SELECT AVG(DATEDIFF(MINUTE, StartTime, EndTime))
                  FROM UserSessions
                  WHERE CAST(StartTime AS DATE) = @Date",
                new { Date = date });
        }

        private async Task<double> GetRetentionRateAsync(SqlConnection connection, DateTime date)
        {
            return await connection.ExecuteScalarAsync<double>(
                @"SELECT (CAST(COUNT(DISTINCT CASE 
                    WHEN ActivityDate >= @Date THEN UserId END) AS FLOAT) /
                    NULLIF(COUNT(DISTINCT UserId), 0)) * 100
                  FROM UserActivities
                  WHERE ActivityDate >= DATEADD(DAY, -7, @Date)",
                new { Date = date });
        }

        private async Task<int> GetNewUsersAsync(SqlConnection connection, DateTime startDate, DateTime endDate)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                  FROM Users 
                  WHERE DateRegistered BETWEEN @StartDate AND @EndDate",
                new { StartDate = startDate, EndDate = endDate });
        }

        private async Task<double> GetAverageTimeSpentPerCourseAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<double>(
                @"SELECT AVG(DATEDIFF(MINUTE, StartTime, EndTime))
                  FROM UserSessions us
                  JOIN Enrollments e ON us.UserId = e.UserId
                  WHERE e.IsCompleted = 1");
        }

        private async Task<int> GetMostActiveTimeSlotAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT TOP 1 DATEPART(HOUR, ActivityDate) as Hour
                  FROM UserActivities
                  GROUP BY DATEPART(HOUR, ActivityDate)
                  ORDER BY COUNT(*) DESC");
        }
        #endregion
    }
} 