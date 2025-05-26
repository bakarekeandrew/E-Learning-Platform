using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
using Dapper;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages
{
    public class UserEngagementModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<UserEngagementModel> _logger;

        public UserEngagementModel(IConfiguration configuration, ILogger<UserEngagementModel> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Initialize all collections
            DailyLabels = new List<string>();
            DailyActiveUsersData = new List<int>();
            Labels = new List<string>();
            ActivityData = new List<int>();
            CompletionData = new List<double>();
            ErrorRateData = new List<double>();
            TimeSlots = new List<string>();
            ActiveStudents = new List<int>();
            ResourceAccess = new List<int>();
            TopEngaged = new List<EngagedStudent>();
            ResourceEffectiveness = new List<ResourceMetric>();
            ModuleProgress = new List<ModuleMetric>();
            RecentAssessments = new List<AssessmentMetric>();
            SkillProgress = new List<SkillMetric>();
            PopularTimeSlots = new List<TimeSlotMetric>();
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

        public List<string> DailyLabels { get; set; }
        public List<int> DailyActiveUsersData { get; set; }
        public List<string> Labels { get; set; }
        public List<int> ActivityData { get; set; }
        public List<double> CompletionData { get; set; }
        public List<double> ErrorRateData { get; set; }
        public List<string> TimeSlots { get; set; }
        public List<int> ActiveStudents { get; set; }
        public List<int> ResourceAccess { get; set; }
        public List<EngagedStudent> TopEngaged { get; set; }
        public List<ResourceMetric> ResourceEffectiveness { get; set; }
        public List<ModuleMetric> ModuleProgress { get; set; }
        public List<AssessmentMetric> RecentAssessments { get; set; }
        public List<SkillMetric> SkillProgress { get; set; }
        public List<TimeSlotMetric> PopularTimeSlots { get; set; }

        public class EngagedStudent
        {
            public string StudentName { get; set; } = string.Empty;
            public int ResourceViews { get; set; }
            public double AverageTimeSpent { get; set; }
        }

        public class ResourceMetric
        {
            public string ResourceName { get; set; } = string.Empty;
            public int Views { get; set; }
            public double EffectivenessScore { get; set; }
        }

        public class ModuleMetric
        {
            public string ModuleName { get; set; } = string.Empty;
            public double CompletionRate { get; set; }
            public int ActiveLearners { get; set; }
        }

        public class AssessmentMetric
        {
            public string AssessmentName { get; set; } = string.Empty;
            public double AverageScore { get; set; }
            public int Submissions { get; set; }
        }

        public class SkillMetric
        {
            public string SkillName { get; set; } = string.Empty;
            public double MasteryRate { get; set; }
            public int LearnersCount { get; set; }
        }

        public class TimeSlotMetric
        {
            public string TimeLabel { get; set; } = string.Empty;
            public int ActiveUsers { get; set; }
            public double EngagementScore { get; set; }
        }

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
                @"SELECT COUNT(*) FROM AppUsers WHERE CAST(DATE_REGISTERED AS DATE) = @Date",
                new { Date = today });
            NewUsersThisWeek = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM AppUsers WHERE CAST(DATE_REGISTERED AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });
            NewUsersThisMonth = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM AppUsers WHERE CAST(DATE_REGISTERED AS DATE) >= @MonthStart",
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
                "SELECT COUNT(DISTINCT USER_ID) FROM USER_SESSIONS WHERE CAST(LOGIN_TIME AS DATE) = @Date",
                new { Date = date });
        }

        private async Task<int> GetWeeklyActiveUsersAsync(SqlConnection connection, DateTime weekStart)
        {
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(DISTINCT USER_ID) FROM USER_SESSIONS WHERE CAST(LOGIN_TIME AS DATE) >= @WeekStart",
                new { WeekStart = weekStart });
        }

        private async Task<int> GetMonthlyActiveUsersAsync(SqlConnection connection, DateTime monthStart)
        {
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(DISTINCT USER_ID) FROM USER_SESSIONS WHERE CAST(LOGIN_TIME AS DATE) >= @MonthStart",
                new { MonthStart = monthStart });
        }

        private async Task<double> GetAverageSessionDurationAsync(SqlConnection connection, DateTime date)
        {
            return await connection.ExecuteScalarAsync<double>(
                @"SELECT AVG(DATEDIFF(MINUTE, LOGIN_TIME, LOGOUT_TIME)) 
                  FROM USER_SESSIONS 
                  WHERE CAST(LOGIN_TIME AS DATE) = @Date AND LOGOUT_TIME IS NOT NULL",
                new { Date = date });
        }

        private async Task<double> GetRetentionRateAsync(SqlConnection connection, DateTime date)
        {
            var result = await connection.QueryFirstOrDefaultAsync<(int Total, int Returning)>(
                @"SELECT 
                    COUNT(DISTINCT USER_ID) as Total,
                    COUNT(DISTINCT CASE WHEN LOGIN_COUNT > 1 THEN USER_ID END) as Returning
                  FROM USER_SESSIONS 
                  WHERE CAST(LOGIN_TIME AS DATE) = @Date",
                new { Date = date });

            return result.Total > 0 ? (double)result.Returning / result.Total * 100 : 0;
        }

        private async Task<int> GetNewUsersAsync(SqlConnection connection, DateTime startDate, DateTime endDate)
        {
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM AppUsers WHERE CAST(DATE_REGISTERED AS DATE) BETWEEN @StartDate AND @EndDate",
                new { StartDate = startDate, EndDate = endDate });
        }

        private async Task<double> GetAverageTimeSpentPerCourseAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<double>(
                @"SELECT AVG(DATEDIFF(MINUTE, START_TIME, END_TIME))
                  FROM COURSE_PROGRESS
                  WHERE END_TIME IS NOT NULL");
        }

        private async Task<int> GetMostActiveTimeSlotAsync(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT TOP 1 DATEPART(HOUR, LOGIN_TIME) as TimeSlot
                  FROM USER_SESSIONS
                  GROUP BY DATEPART(HOUR, LOGIN_TIME)
                  ORDER BY COUNT(*) DESC");
        }
        #endregion
    }
} 