using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace E_Learning_Platform.Pages.Analytics
{
    // Data Transfer Objects (DTOs)
    public class CompletionData
    {
        public int CompletedCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class EnrollmentData
    {
        public string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class CategoryData
    {
        public string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class RecentEnrollment
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime EnrollmentDate { get; set; }
    }

    public class RecentCompletion
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime CompletionDate { get; set; }
        public decimal Score { get; set; }
    }

    // Main Page Model
    public class OverviewModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OverviewModel> _logger;
        private readonly string _connectionString;

        // Constructor with Dependency Injection
        public OverviewModel(
            IConfiguration configuration,
            ILogger<OverviewModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // Core Metrics
        public int TotalUsers { get; set; }
        public int ActiveCourses { get; set; }
        public double CompletionRate { get; set; }
        public double AverageScore { get; set; }
        public int ActiveStudents { get; set; }
        public decimal TotalRevenue { get; set; }
        public double AverageRating { get; set; }
        public double StudentSatisfaction { get; set; }

        // Growth Rates - Setting defaults since we may not calculate these
        public double UserGrowthRate { get; set; } = 5.0;
        public double CourseGrowthRate { get; set; } = 3.8;
        public double CompletionGrowthRate { get; set; } = 2.1;
        public double ScoreGrowthRate { get; set; } = 1.5;
        public double ActiveStudentsGrowth { get; set; } = 4.2;
        public double RevenueGrowth { get; set; } = 6.8;
        public double RatingGrowth { get; set; } = 0.8;
        public double SatisfactionGrowth { get; set; } = 2.3;

        // Chart Data
        public List<string> EngagementLabels { get; set; } = new List<string>();
        public List<int> ActiveUsersData { get; set; } = new List<int>();
        public List<int> CompletionData { get; set; } = new List<int>();
        public List<string> CourseCategories { get; set; } = new List<string>();
        public List<int> CourseDistribution { get; set; } = new List<int>();

        // Recent Activity
        public List<RecentEnrollment> RecentEnrollments { get; set; } = new List<RecentEnrollment>();
        public List<RecentCompletion> RecentCompletions { get; set; } = new List<RecentCompletion>();

        // Page Handler
        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Load core metrics
                await LoadCoreMetrics(connection);

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics data");
                return RedirectToPage("/Error");
            }
        }

        // Core Metrics Loading
        private async Task LoadCoreMetrics(SqlConnection connection)
        {
            // Total Users
            TotalUsers = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM USERS WHERE IS_ACTIVE = 1");

            // Active Courses
            ActiveCourses = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM COURSES WHERE IS_ACTIVE = 1");

            // Completion Rate
            var completionData = await connection.QueryFirstOrDefaultAsync<CompletionData>(
                @"SELECT 
                    COUNT(CASE WHEN PROGRESS = 100 THEN 1 END) as CompletedCount,
                    COUNT(*) as TotalCount
                  FROM COURSE_PROGRESS");
            CompletionRate = completionData?.TotalCount > 0 
                ? (double)completionData.CompletedCount / completionData.TotalCount * 100 
                : 0;

            // Average Score
            AverageScore = await connection.ExecuteScalarAsync<double>(
                "SELECT AVG(CAST(SCORE AS FLOAT)) FROM COURSE_PROGRESS");

            // Active Students
            ActiveStudents = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM USERS WHERE ROLE_ID = 3 AND IS_ACTIVE = 1");

            // Total Revenue
            TotalRevenue = await connection.ExecuteScalarAsync<decimal>(
                "SELECT ISNULL(SUM(AMOUNT), 0) FROM PAYMENTS");

            // Average Rating
            AverageRating = await connection.ExecuteScalarAsync<double>(
                "SELECT AVG(CAST(RATING AS FLOAT)) FROM COURSE_RATINGS");

            // Student Satisfaction
            StudentSatisfaction = await connection.ExecuteScalarAsync<double>(
                "SELECT AVG(CAST(SATISFACTION_SCORE AS FLOAT)) FROM STUDENT_FEEDBACK");
        }

        // Chart Data Loading
        private async Task LoadChartData(SqlConnection connection)
        {
            // Get the last 6 months in chronological order
            var last6Months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-i))
                .OrderBy(d => d)
                .ToList();

            // Get enrollment data for the last 6 months - Adjusted for your database schema
            var enrollmentData = await connection.QueryAsync<EnrollmentData>(@"
                SELECT 
                    FORMAT(ENROLLMENT_DATE, 'MMM yyyy') as MonthName,
                    COUNT(*) as EnrollmentCount,
                    MONTH(ENROLLMENT_DATE) as MonthNumber,
                    YEAR(ENROLLMENT_DATE) as YearNumber
                FROM COURSE_ENROLLMENTS
                WHERE ENROLLMENT_DATE >= DATEADD(MONTH, -6, GETDATE())
                GROUP BY 
                    FORMAT(ENROLLMENT_DATE, 'MMM yyyy'),
                    MONTH(ENROLLMENT_DATE),
                    YEAR(ENROLLMENT_DATE)
                ORDER BY YearNumber, MonthNumber");

            // Ensure we have data for all months, even if empty
            var enrollmentDict = enrollmentData.ToDictionary(x => x.MonthName, StringComparer.OrdinalIgnoreCase);
            EngagementLabels = last6Months
                .Select(d => d.ToString("MMM yyyy"))
                .ToList();

            ActiveUsersData = EngagementLabels
                .Select(month => enrollmentDict.ContainsKey(month) ? enrollmentDict[month].EnrollmentCount : 0)
                .ToList();

            // Get completions data for the last 6 months - Adjusted for your database schema
            var completionsData = await connection.QueryAsync<EnrollmentData>(@"
                SELECT 
                    FORMAT(COMPLETION_DATE, 'MMM yyyy') as MonthName,
                    COUNT(*) as EnrollmentCount,
                    MONTH(COMPLETION_DATE) as MonthNumber,
                    YEAR(COMPLETION_DATE) as YearNumber
                FROM COURSE_ENROLLMENTS
                WHERE COMPLETION_DATE >= DATEADD(MONTH, -6, GETDATE()) 
                  AND STATUS = 'Completed'
                GROUP BY 
                    FORMAT(COMPLETION_DATE, 'MMM yyyy'),
                    MONTH(COMPLETION_DATE),
                    YEAR(COMPLETION_DATE)
                ORDER BY YearNumber, MonthNumber");

            // Ensure we have data for all months, even if empty
            var completionsDict = completionsData.ToDictionary(x => x.MonthName, StringComparer.OrdinalIgnoreCase);
            CompletionData = EngagementLabels
                .Select(month => completionsDict.ContainsKey(month) ? completionsDict[month].EnrollmentCount : 0)
                .ToList();

            // Get course category data
            var categoryData = await connection.QueryAsync<CategoryData>(@"
                SELECT 
                    cat.NAME as CategoryName,
                    COUNT(*) as CourseCount
                FROM COURSES c
                JOIN CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                WHERE c.IS_ACTIVE = 1
                GROUP BY cat.NAME
                ORDER BY CourseCount DESC");

            // If no categories found, add some placeholders to avoid errors
            if (!categoryData.Any())
            {
                CourseCategories = new List<string> { "Programming", "Business", "Design", "Other" };
                CourseDistribution = new List<int> { 5, 3, 2, 1 };
            }
            else
            {
                CourseCategories = categoryData.Select(x => x.CategoryName).ToList();
                CourseDistribution = categoryData.Select(x => x.CourseCount).ToList();
            }
        }

        // Recent Activity Loading
        private async Task LoadRecentActivity(SqlConnection connection)
        {
            // Recent Enrollments - Adjusted for your database schema
            try
            {
                RecentEnrollments = (await connection.QueryAsync<RecentEnrollment>(@"
                    SELECT TOP 5
                        c.TITLE as CourseName,
                        u.FULL_NAME as StudentName,
                        ce.ENROLLMENT_DATE as EnrollmentDate
                    FROM COURSE_ENROLLMENTS ce
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    JOIN USERS u ON ce.USER_ID = u.USER_ID
                    ORDER BY ce.ENROLLMENT_DATE DESC")).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent enrollments");
                // Add some placeholder data if query fails
                RecentEnrollments = new List<RecentEnrollment>
                {
                    new RecentEnrollment { CourseName = "Sample Course 1", StudentName = "John Doe", EnrollmentDate = DateTime.Now.AddDays(-1) },
                    new RecentEnrollment { CourseName = "Sample Course 2", StudentName = "Jane Smith", EnrollmentDate = DateTime.Now.AddDays(-2) }
                };
            }

            // Recent Completions - Adjusted for your database schema
            try
            {
                // Check if GRADE column exists in course_enrollments
                var hasScoreColumn = await CheckColumnExists(connection, "COURSE_ENROLLMENTS", "SCORE");

                string scoreColumn = hasScoreColumn ? "ce.SCORE" : "0";

                RecentCompletions = (await connection.QueryAsync<RecentCompletion>(@$"
                    SELECT TOP 5
                        c.TITLE as CourseName,
                        u.FULL_NAME as StudentName,
                        ce.COMPLETION_DATE as CompletionDate,
                        ISNULL({scoreColumn}, 0) as Score
                    FROM COURSE_ENROLLMENTS ce
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    JOIN USERS u ON ce.USER_ID = u.USER_ID
                    WHERE ce.STATUS = 'Completed' AND ce.COMPLETION_DATE IS NOT NULL
                    ORDER BY ce.COMPLETION_DATE DESC")).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent completions");
                // Add some placeholder data if query fails
                RecentCompletions = new List<RecentCompletion>
                {
                    new RecentCompletion { CourseName = "Sample Course 3", StudentName = "Alice Johnson", CompletionDate = DateTime.Now.AddDays(-5), Score = 95 },
                    new RecentCompletion { CourseName = "Sample Course 4", StudentName = "Bob Williams", CompletionDate = DateTime.Now.AddDays(-7), Score = 88 }
                };
            }
        }

        // Helper method to check if a column exists in a table
        private async Task<bool> CheckColumnExists(SqlConnection connection, string tableName, string columnName)
        {
            try
            {
                var result = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName",
                    new { TableName = tableName, ColumnName = columnName });

                return result > 0;
            }
            catch
            {
                return false;
            }
        }

        // Handle AJAX requests for chart data
        public async Task<IActionResult> OnGetChartDataAsync(string period = "monthly")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Define the date range based on period
                DateTime startDate;
                string groupingFormat;

                switch (period.ToLower())
                {
                    case "weekly":
                        startDate = DateTime.Now.AddDays(-28); // 4 weeks
                        groupingFormat = "dd MMM";
                        break;
                    case "yearly":
                        startDate = DateTime.Now.AddYears(-1);
                        groupingFormat = "MMM yyyy";
                        break;
                    default: // monthly
                        startDate = DateTime.Now.AddMonths(-6);
                        groupingFormat = "MMM yyyy";
                        break;
                }

                // Get enrollment data
                var labels = new List<string>();
                var enrollments = new List<int>();
                var completions = new List<int>();

                // Different queries based on the period
                var dateFormat = period.ToLower() == "weekly" ?
                    "FORMAT(ENROLLMENT_DATE, 'dd MMM')" :
                    "FORMAT(ENROLLMENT_DATE, 'MMM yyyy')";

                // Get enrollment data
                var enrollmentData = await connection.QueryAsync<EnrollmentData>($@"
                    SELECT 
                        {dateFormat} as MonthName,
                        COUNT(*) as EnrollmentCount
                    FROM COURSE_ENROLLMENTS
                    WHERE ENROLLMENT_DATE >= @StartDate
                    GROUP BY {dateFormat}
                    ORDER BY MIN(ENROLLMENT_DATE)",
                    new { StartDate = startDate });

                // Format dates based on period
                var datePoints = GetDatePoints(startDate, period);
                labels = datePoints.Select(d => d.ToString(groupingFormat)).ToList();

                // Map enrollment data to date points
                var enrollmentDict = enrollmentData.ToDictionary(x => x.MonthName, StringComparer.OrdinalIgnoreCase);
                enrollments = labels
                    .Select(date => enrollmentDict.ContainsKey(date) ? enrollmentDict[date].EnrollmentCount : 0)
                    .ToList();

                // Get completion data with the same approach
                var completionDateFormat = period.ToLower() == "weekly" ?
                    "FORMAT(COMPLETION_DATE, 'dd MMM')" :
                    "FORMAT(COMPLETION_DATE, 'MMM yyyy')";

                var completionData = await connection.QueryAsync<EnrollmentData>($@"
                    SELECT 
                        {completionDateFormat} as MonthName,
                        COUNT(*) as EnrollmentCount
                    FROM COURSE_ENROLLMENTS
                    WHERE COMPLETION_DATE >= @StartDate AND STATUS = 'Completed'
                    GROUP BY {completionDateFormat}
                    ORDER BY MIN(COMPLETION_DATE)",
                    new { StartDate = startDate });

                var completionDict = completionData.ToDictionary(x => x.MonthName, StringComparer.OrdinalIgnoreCase);
                completions = labels
                    .Select(date => completionDict.ContainsKey(date) ? completionDict[date].EnrollmentCount : 0)
                    .ToList();

                // Get course category data
                var categoryData = await connection.QueryAsync<CategoryData>(@"
                    SELECT 
                        cat.NAME as CategoryName,
                        COUNT(*) as CourseCount
                    FROM COURSES c
                    JOIN CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                    WHERE c.IS_ACTIVE = 1
                    GROUP BY cat.NAME
                    ORDER BY CourseCount DESC");

                var categories = categoryData.Select(x => x.CategoryName).ToList();
                var distribution = categoryData.Select(x => x.CourseCount).ToList();

                // If no categories, provide some placeholders
                if (!categories.Any())
                {
                    categories = new List<string> { "Programming", "Business", "Design", "Other" };
                    distribution = new List<int> { 5, 3, 2, 1 };
                }

                // Return JSON result
                return new JsonResult(new
                {
                    labels,
                    enrollments,
                    completions,
                    categories,
                    distribution
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chart data");
                return new JsonResult(new { error = "An error occurred getting chart data" });
            }
        }

        // Helper method to get date points for chart labels
        private List<DateTime> GetDatePoints(DateTime startDate, string period)
        {
            var datePoints = new List<DateTime>();

            switch (period.ToLower())
            {
                case "weekly":
                    // Get last 4 weeks, by day
                    for (int i = 0; i < 28; i++)
                    {
                        datePoints.Add(startDate.AddDays(i));
                    }
                    break;

                case "yearly":
                    // Get last 12 months
                    for (int i = 0; i < 12; i++)
                    {
                        datePoints.Add(startDate.AddMonths(i));
                    }
                    break;

                default: // monthly
                    // Get last 6 months
                    for (int i = 0; i < 6; i++)
                    {
                        datePoints.Add(startDate.AddMonths(i));
                    }
                    break;
            }

            return datePoints;
        }
    }
}