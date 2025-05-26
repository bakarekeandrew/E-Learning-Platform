using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics.PerformanceData;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages.Analytics
{
    // Model class for system metrics data
    public class SystemMetricsData
    {
        public DateTime Timestamp { get; set; }
        public decimal CPUUsage { get; set; }
        public decimal MemoryUsage { get; set; }
        public int DatabaseConnections { get; set; }
        public int ResponseTime { get; set; }
        public int RequestsPerMinute { get; set; }
        public int ActiveSessions { get; set; }
        public string Notes { get; set; }
    }

    public class OverviewModel : PageModel
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OverviewModel> _logger;
        private readonly string _connectionString;

        public OverviewModel(IConfiguration configuration, ILogger<OverviewModel> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        // Core Metrics
        public int TotalUsers { get; set; }
        public int ActiveCourses { get; set; }
        public int TotalEnrollments { get; set; }
        public double CompletionRate { get; set; }
        public int ActiveStudents { get; set; }
        public double AverageProgress { get; set; }
        public double AverageRating { get; set; }

        // Growth Rates
        public double UserGrowthRate { get; set; }
        public double CourseGrowthRate { get; set; }
        public double EnrollmentGrowthRate { get; set; }
        public double RatingGrowthRate { get; set; }

        // User Engagement Metrics
        public int DailyActiveUsers { get; set; }
        public int AverageSessionDuration { get; set; }
        public double RetentionRate { get; set; }
        public int NewUsersToday { get; set; }
        public List<string> ActivityLabels { get; set; } = new List<string>();
        public List<int> ActivityData { get; set; } = new List<int>();
        public List<string> UserRoleLabels { get; set; } = new List<string>();
        public List<int> UserRoleData { get; set; } = new List<int>();

        // Course Performance Metrics
        public List<TopCourseViewModel> TopCourses { get; set; } = new List<TopCourseViewModel>();
        public List<string> CompletionLabels { get; set; } = new List<string>();
        public List<double> CompletionData { get; set; } = new List<double>();
        public List<string> RatingLabels { get; set; } = new List<string>();
        public List<int> RatingDistribution { get; set; } = new List<int>();
        public List<string> CategoryLabels { get; set; } = new List<string>();
        public List<int> CategoryData { get; set; } = new List<int>();

        // System Performance Metrics
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public int DatabaseConnections { get; set; }
        public int AverageResponseTime { get; set; }
        public List<string> ResponseTimeLabels { get; set; } = new List<string>();
        public List<int> ResponseTimeData { get; set; } = new List<int>();
        public List<string> ErrorRateLabels { get; set; } = new List<string>();
        public List<double> ErrorRateData { get; set; } = new List<double>();
        public List<SystemError> RecentErrors { get; set; } = new List<SystemError>();

        private async Task EnsureTestDataExists(SqlConnection connection)
        {
            try
            {
                // Check if we have any system metrics
                var hasMetrics = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM SYSTEM_METRICS") > 0;

                if (!hasMetrics)
                {
                    // Add sample system metrics for the last 24 hours
                    var random = new Random();
                    for (int i = 24; i >= 0; i--)
                    {
                        await connection.ExecuteAsync(
                            "EXEC [dbo].[InsertSystemMetrics] @CPUUsage, @MemoryUsage, @DatabaseConnections, @ResponseTime, @RequestsPerMinute, @ActiveSessions, @Notes",
                            new
                            {
                                CPUUsage = Math.Round(random.Next(20, 80) / 1m, 2), // Convert to decimal with 2 decimal places
                                MemoryUsage = Math.Round(random.Next(30, 90) / 1m, 2), // Convert to decimal with 2 decimal places
                                DatabaseConnections = random.Next(1, 20),
                                ResponseTime = random.Next(50, 500),
                                RequestsPerMinute = random.Next(10, 100),
                                ActiveSessions = random.Next(5, 50),
                                Notes = "Sample data"
                            });
                    }
                }

                // Check if we have any error metrics
                var hasErrorMetrics = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM ERROR_METRICS") > 0;

                if (!hasErrorMetrics)
                {
                    // Add sample error metrics for the last 24 hours
                    var random = new Random();
                    for (int i = 24; i >= 0; i--)
                    {
                        var totalRequests = random.Next(1000, 5000);
                        var errorCount = random.Next(0, 50);
                        var errorRate = (double)errorCount / totalRequests;

                        await connection.ExecuteAsync(
                            "EXEC [dbo].[InsertErrorMetrics] @ErrorRate, @TotalRequests, @ErrorCount, @TimeWindow, @Notes",
                            new
                            {
                                ErrorRate = errorRate,
                                TotalRequests = totalRequests,
                                ErrorCount = errorCount,
                                TimeWindow = "1h",
                                Notes = "Sample data"
                            });
                    }
                }

                // Check if we have any error logs
                var hasErrorLogs = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM ERROR_LOGS") > 0;

                if (!hasErrorLogs)
                {
                    // Add sample error logs
                    var errorTypes = new[] { "Error", "Warning", "Info" };
                    var paths = new[] { "/api/users", "/api/courses", "/api/analytics", "/api/auth" };
                    var messages = new[] {
                        "Database connection failed",
                        "Invalid request parameters",
                        "Authentication failed",
                        "Resource not found",
                        "Operation timeout"
                    };

                    var random = new Random();
                    for (int i = 0; i < 10; i++)
                    {
                        await connection.ExecuteAsync(
                            "EXEC [dbo].[LogError] @ErrorType, @Severity, @Path, @Message, @StackTrace, @UserID, @RequestData",
                            new
                            {
                                ErrorType = errorTypes[random.Next(errorTypes.Length)],
                                Severity = "Error",
                                Path = paths[random.Next(paths.Length)],
                                Message = messages[random.Next(messages.Length)],
                                StackTrace = "Sample stack trace",
                                UserID = random.Next(1, 100),
                                RequestData = "Sample request data"
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding test data");
            }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Ensure we have test data
                await EnsureTestDataExists(connection);

                // Load core metrics
                await LoadCoreMetrics(connection);
                
                // Load user engagement metrics
                await LoadUserEngagementMetrics(connection);
                
                // Load course performance metrics
                await LoadCoursePerformanceMetrics(connection);

                // Load system performance metrics
                await LoadSystemPerformanceMetrics(connection);

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

            // Total Enrollments (from COURSE_ENROLLMENTS table)
            TotalEnrollments = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM COURSE_ENROLLMENTS");

            // Completion Rate (using COURSE_ENROLLMENTS status and COURSE_PROGRESS)
            var completionData = await connection.QueryFirstOrDefaultAsync<CompletionData>(
                @"WITH EnrollmentProgress AS (
                    SELECT 
                        ce.ENROLLMENT_ID,
                        ce.STATUS,
                        ISNULL(cp.PROGRESS, 0) as PROGRESS
                    FROM COURSE_ENROLLMENTS ce
                    LEFT JOIN COURSE_PROGRESS cp ON cp.USER_ID = ce.USER_ID 
                        AND cp.COURSE_ID = ce.COURSE_ID
                )
                SELECT 
                    COUNT(*) as TotalCount,
                    COUNT(CASE WHEN STATUS = 'Completed' THEN 1 END) as CompletedCount,
                    ISNULL(AVG(CAST(PROGRESS AS FLOAT)), 0) as AverageProgress
                FROM EnrollmentProgress");

            CompletionRate = completionData?.TotalCount > 0 
                ? (double)completionData.CompletedCount / completionData.TotalCount * 100 
                : 0;

            // Average Progress (from COURSE_PROGRESS table)
            AverageProgress = await connection.ExecuteScalarAsync<double>(
                @"SELECT ISNULL(AVG(CAST(PROGRESS AS FLOAT)), 0) 
                  FROM COURSE_PROGRESS 
                  WHERE PROGRESS IS NOT NULL");

            // Active Students (with recent activity in last 30 days)
            ActiveStudents = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT cp.USER_ID)
                  FROM COURSE_PROGRESS cp
                  JOIN USERS u ON cp.USER_ID = u.USER_ID
                  WHERE u.ROLE_ID = 3 
                  AND u.IS_ACTIVE = 1
                  AND cp.LAST_ACCESSED >= DATEADD(DAY, -30, GETDATE())");

            // Average Rating
            AverageRating = await connection.ExecuteScalarAsync<double>(
                "SELECT ISNULL(AVG(CAST(RATING AS FLOAT)), 0) FROM REVIEWS");

            // Calculate growth rates
            await CalculateGrowthRates(connection);
        }

        private async Task LoadUserEngagementMetrics(SqlConnection connection)
        {
            var today = DateTime.Today;

            // Daily Active Users (users who accessed courses today)
            DailyActiveUsers = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT USER_ID) 
                FROM COURSE_PROGRESS 
                WHERE CAST(LAST_ACCESSED AS DATE) = @Today",
                new { Today = today });

            // Average Session Duration (estimate based on course progress)
            AverageSessionDuration = 30; // Default to 30 minutes as we don't have actual session data

            // Retention Rate (users who accessed courses within last 7 days / total users)
            var activeUsers = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(DISTINCT USER_ID)
                FROM COURSE_PROGRESS
                WHERE LAST_ACCESSED >= DATEADD(DAY, -7, GETDATE())");
            RetentionRate = TotalUsers > 0 ? (double)activeUsers / TotalUsers : 0;

            // New Users Today
            NewUsersToday = await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                FROM USERS 
                WHERE CAST(DATE_REGISTERED AS DATE) = @Today",
                new { Today = today });

            // User Activity Data (last 7 days)
            var activityData = await connection.QueryAsync<DailyActivity>(
                @"SELECT 
                    CAST(LAST_ACCESSED AS DATE) AS Date,
                    COUNT(DISTINCT USER_ID) AS UserCount
                FROM COURSE_PROGRESS
                WHERE LAST_ACCESSED >= DATEADD(DAY, -7, GETDATE())
                GROUP BY CAST(LAST_ACCESSED AS DATE)
                ORDER BY CAST(LAST_ACCESSED AS DATE)");

            ActivityLabels = new List<string>();
            ActivityData = new List<int>();

            // Fill in any missing days with zero values
            var currentDate = DateTime.Today.AddDays(-7);
            while (currentDate <= DateTime.Today)
            {
                var dayData = activityData.FirstOrDefault(d => d.Date.Date == currentDate.Date);
                ActivityLabels.Add(currentDate.ToString("MMM dd"));
                ActivityData.Add(dayData?.UserCount ?? 0);
                currentDate = currentDate.AddDays(1);
            }

            // User Role Distribution
            var roleData = await connection.QueryAsync<RoleDistribution>(
                @"SELECT 
                    CASE ROLE_ID 
                        WHEN 1 THEN 'Admin'
                        WHEN 2 THEN 'Instructor'
                        WHEN 3 THEN 'Student'
                        ELSE 'Other'
                    END AS Role,
                    COUNT(*) AS Count
                FROM USERS
                WHERE IS_ACTIVE = 1
                GROUP BY ROLE_ID");

            UserRoleLabels = roleData.Select(r => r.Role).ToList();
            UserRoleData = roleData.Select(r => r.Count).ToList();
        }

        private async Task LoadCoursePerformanceMetrics(SqlConnection connection)
        {
            // Top Performing Courses
            TopCourses = (await connection.QueryAsync<TopCourseViewModel>(
                @"SELECT TOP 5
                    c.COURSE_ID,
                    c.TITLE,
                    c.THUMBNAIL_URL,
                    u.FULL_NAME AS InstructorName,
                    (SELECT COUNT(*) FROM COURSE_ENROLLMENTS ce WHERE ce.COURSE_ID = c.COURSE_ID) AS Enrollments,
                    ISNULL(CAST(
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS ce 
                         WHERE ce.COURSE_ID = c.COURSE_ID AND ce.STATUS = 'Completed')
                        AS FLOAT) /
                        NULLIF((SELECT COUNT(*) FROM COURSE_ENROLLMENTS ce 
                               WHERE ce.COURSE_ID = c.COURSE_ID), 0), 0) AS CompletionRate,
                    ISNULL((SELECT AVG(CAST(RATING AS FLOAT)) 
                           FROM REVIEWS r 
                           WHERE r.COURSE_ID = c.COURSE_ID), 0) AS Rating
                FROM COURSES c
                JOIN USERS u ON c.CREATED_BY = u.USER_ID
                WHERE c.IS_ACTIVE = 1
                ORDER BY Rating DESC, Enrollments DESC")).AsList();

            // Course Completion Trends (last 6 months)
            var completionTrends = await connection.QueryAsync<CompletionTrend>(
                @"WITH Months AS (
                    SELECT TOP 6
                        DATEADD(MONTH, -number, GETDATE()) AS MonthDate
                    FROM master.dbo.spt_values
                    WHERE type = 'P'
                    ORDER BY number
                )
                SELECT 
                    FORMAT(m.MonthDate, 'MMM yyyy') AS Month,
                    COUNT(DISTINCT ce.ENROLLMENT_ID) AS CompletionCount
                FROM Months m
                LEFT JOIN COURSE_ENROLLMENTS ce ON 
                    FORMAT(ce.COMPLETION_DATE, 'yyyyMM') = FORMAT(m.MonthDate, 'yyyyMM')
                    AND ce.STATUS = 'Completed'
                GROUP BY m.MonthDate, FORMAT(m.MonthDate, 'MMM yyyy')
                ORDER BY m.MonthDate");

            CompletionLabels = completionTrends.Select(t => t.Month).ToList();
            CompletionData = completionTrends.Select(t => (double)t.CompletionCount).ToList();

            // Course Categories Distribution
            var categoryData = await connection.QueryAsync<CategoryDistribution>(
                @"WITH CategoryCounts AS (
                SELECT 
                        cc.CATEGORY_ID,
                        cc.NAME as CategoryName,
                        COUNT(c.COURSE_ID) as CourseCount,
                        SUM(
                            CASE 
                                WHEN c.IS_ACTIVE = 1 THEN 1 
                                ELSE 0 
                            END
                        ) as ActiveCourseCount
                    FROM COURSE_CATEGORIES cc
                    LEFT JOIN COURSES c ON cc.CATEGORY_ID = c.CATEGORY_ID
                    GROUP BY cc.CATEGORY_ID, cc.NAME
                )
                SELECT TOP 5
                    CategoryName,
                    CourseCount
                FROM CategoryCounts
                ORDER BY ActiveCourseCount DESC");

            CategoryLabels = categoryData.Select(c => c.CategoryName).ToList();
            CategoryData = categoryData.Select(c => c.CourseCount).ToList();

            // Rating Distribution
            var ratingDist = await connection.QueryAsync<RatingDistribution>(
                @"WITH RatingScale AS (
                    SELECT number AS Rating
                    FROM master.dbo.spt_values
                    WHERE type = 'P' AND number BETWEEN 1 AND 5
                )
                SELECT 
                    rs.Rating,
                    COUNT(r.RATING) AS Count
                FROM RatingScale rs
                LEFT JOIN REVIEWS r ON r.RATING = rs.Rating
                GROUP BY rs.Rating
                ORDER BY rs.Rating");

            RatingLabels = ratingDist.Select(r => r.Rating.ToString() + " Star").ToList();
            RatingDistribution = ratingDist.Select(r => r.Count).ToList();
        }

        private async Task CalculateGrowthRates(SqlConnection connection)
        {
            var lastMonth = DateTime.Now.AddMonths(-1);
            
            // User growth
            var currentUsers = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM USERS WHERE DATE_REGISTERED >= @StartDate",
                new { StartDate = DateTime.Now.AddDays(-30) });
            
            var previousUsers = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM USERS WHERE DATE_REGISTERED >= @StartDate AND DATE_REGISTERED < @EndDate",
                new { StartDate = DateTime.Now.AddDays(-60), EndDate = DateTime.Now.AddDays(-30) });

            UserGrowthRate = previousUsers > 0 
                ? ((double)currentUsers - previousUsers) / previousUsers 
                : 0;

            // Course growth
            var currentCourses = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM COURSES WHERE CREATION_DATE >= @StartDate",
                new { StartDate = DateTime.Now.AddDays(-30) });
            
            var previousCourses = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM COURSES WHERE CREATION_DATE >= @StartDate AND CREATION_DATE < @EndDate",
                new { StartDate = DateTime.Now.AddDays(-60), EndDate = DateTime.Now.AddDays(-30) });

            CourseGrowthRate = previousCourses > 0 
                ? ((double)currentCourses - previousCourses) / previousCourses 
                : 0;

            // Enrollment growth (using LAST_ACCESSED as enrollment date)
            var currentEnrollments = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(DISTINCT USER_ID) FROM COURSE_PROGRESS WHERE LAST_ACCESSED >= @StartDate",
                new { StartDate = DateTime.Now.AddDays(-30) });
            
            var previousEnrollments = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(DISTINCT USER_ID) FROM COURSE_PROGRESS WHERE LAST_ACCESSED >= @StartDate AND LAST_ACCESSED < @EndDate",
                new { StartDate = DateTime.Now.AddDays(-60), EndDate = DateTime.Now.AddDays(-30) });

            EnrollmentGrowthRate = previousEnrollments > 0 
                ? ((double)currentEnrollments - previousEnrollments) / previousEnrollments 
                : 0;

            // Rating growth
            var currentRating = await connection.ExecuteScalarAsync<double>(
                "SELECT ISNULL(AVG(CAST(RATING AS FLOAT)), 0) FROM REVIEWS WHERE REVIEW_DATE >= @StartDate",
                new { StartDate = DateTime.Now.AddDays(-30) });
            
            var previousRating = await connection.ExecuteScalarAsync<double>(
                "SELECT ISNULL(AVG(CAST(RATING AS FLOAT)), 0) FROM REVIEWS WHERE REVIEW_DATE >= @StartDate AND REVIEW_DATE < @EndDate",
                new { StartDate = DateTime.Now.AddDays(-60), EndDate = DateTime.Now.AddDays(-30) });

            RatingGrowthRate = previousRating > 0 
                ? ((double)currentRating - previousRating) / previousRating 
                : 0;
        }

        private async Task LoadSystemPerformanceMetrics(SqlConnection connection)
        {
            try
            {
                // Get CPU and Memory usage using Process info as primary method
                try
                {
                    using var process = Process.GetCurrentProcess();
                    
                    // CPU Usage (approximate)
                    var startTime = DateTime.UtcNow;
                    var startCpuUsage = process.TotalProcessorTime;
                    await Task.Delay(500); // Wait for 500ms to get a sample
                    var endTime = DateTime.UtcNow;
                    var endCpuUsage = process.TotalProcessorTime;
                    var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                    var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    
                    CpuUsage = Math.Min(1, Math.Max(0, cpuUsageTotal));

                    // Memory Usage
                    var totalPhysicalMemory = process.WorkingSet64;
                    var systemMemory = process.PrivateMemorySize64;
                    MemoryUsage = (double)totalPhysicalMemory / systemMemory;

                    // Database Connections (using SQL Server DMV)
                    DatabaseConnections = await connection.ExecuteScalarAsync<int>(
                        @"SELECT COUNT(*) 
                          FROM sys.dm_exec_connections 
                          WHERE session_id > 50"); // Exclude system sessions

                    // Store the current metrics
                    await connection.ExecuteAsync(
                        "EXEC [dbo].[InsertSystemMetrics] @CPUUsage, @MemoryUsage, @DatabaseConnections, @ResponseTime, @RequestsPerMinute, @ActiveSessions, @Notes",
                        new
                        {
                            CPUUsage = Math.Round(Convert.ToDecimal(CpuUsage) * 100m, 2),
                            MemoryUsage = Math.Round(Convert.ToDecimal(MemoryUsage) * 100m, 2),
                            DatabaseConnections = DatabaseConnections,
                            ResponseTime = AverageResponseTime,
                            RequestsPerMinute = await GetRequestsPerMinute(connection),
                            ActiveSessions = await GetActiveSessions(connection),
                            Notes = "Metrics collected from process info"
                        });
            }
            catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get system metrics, using default values");
                    CpuUsage = 0.3;
                    MemoryUsage = 0.4;
                    DatabaseConnections = 1;
                }

                // Get latest system metrics from the table
                var latestMetrics = await connection.QueryFirstOrDefaultAsync<SystemMetricsData>(
                    @"SELECT TOP 1 *
                    FROM [dbo].[SYSTEM_METRICS]
                    ORDER BY [Timestamp] DESC");

                if (latestMetrics != null)
                {
                    _logger.LogInformation($"CPU Usage: {latestMetrics.CPUUsage}, Memory Usage: {latestMetrics.MemoryUsage}");
                    CpuUsage = (double)(latestMetrics.CPUUsage / 100m); // Convert decimal percentage to double
                    MemoryUsage = (double)(latestMetrics.MemoryUsage / 100m); // Convert decimal percentage to double
                    DatabaseConnections = latestMetrics.DatabaseConnections;
                    AverageResponseTime = latestMetrics.ResponseTime;
                }
                else
                {
                    DatabaseConnections = 5;
                    AverageResponseTime = 150;
                }

                // Get response time trend (last 24 hours)
                var responseTimeTrend = await connection.QueryAsync<ResponseTimeData>(
                    @"SELECT 
                        FORMAT([Timestamp], 'HH:mm') as TimeLabel,
                        [ResponseTime]
                    FROM [dbo].[SYSTEM_METRICS]
                    WHERE [Timestamp] >= DATEADD(HOUR, -24, GETDATE())
                    ORDER BY [Timestamp]");

                _logger.LogInformation($"Response time data points retrieved: {responseTimeTrend.Count()}");
                _logger.LogInformation($"First response time value: {(responseTimeTrend.Any() ? responseTimeTrend.First().ResponseTime.ToString() : "none")}");

                ResponseTimeLabels = responseTimeTrend.Select(r => r.TimeLabel).ToList();
                ResponseTimeData = responseTimeTrend.Select(r => r.ResponseTime).ToList();

                // Get error rate trend (last 24 hours)
                var errorRateTrend = await connection.QueryAsync<ErrorRateData>(
                    @"SELECT 
                        FORMAT([Timestamp], 'HH:mm') as TimeLabel,
                        [ErrorRate]
                    FROM [dbo].[ERROR_METRICS]
                    WHERE [Timestamp] >= DATEADD(HOUR, -24, GETDATE())
                    ORDER BY [Timestamp]");

                _logger.LogInformation($"Error rate data points retrieved: {errorRateTrend.Count()}");
                _logger.LogInformation($"First error rate value: {(errorRateTrend.Any() ? errorRateTrend.First().ErrorRate.ToString() : "none")}");

                ErrorRateLabels = errorRateTrend.Select(e => e.TimeLabel).ToList();
                ErrorRateData = errorRateTrend.Select(e => e.ErrorRate).ToList();

                // Get recent errors
                RecentErrors = (await connection.QueryAsync<SystemError>(
                    @"SELECT TOP 10
                        [Timestamp],
                        [ErrorType],
                        [Path],
                        [Message]
                    FROM [dbo].[ERROR_LOGS]
                    ORDER BY [Timestamp] DESC")).ToList();

                _logger.LogInformation($"Recent errors retrieved: {RecentErrors.Count}");

                // If no data exists yet, add sample data
                if (!ResponseTimeLabels.Any())
                {
                    var rnd = new Random();
                    for (int i = 24; i >= 0; i--)
                    {
                        ResponseTimeLabels.Add(DateTime.Now.AddHours(-i).ToString("HH:mm"));
                        ResponseTimeData.Add(rnd.Next(100, 300));
                    }
                }

                if (!ErrorRateLabels.Any())
                {
                    var rnd = new Random();
                    for (int i = 24; i >= 0; i--)
                    {
                        ErrorRateLabels.Add(DateTime.Now.AddHours(-i).ToString("HH:mm"));
                        ErrorRateData.Add(rnd.NextDouble() * 0.02);
                    }
                }

                if (!RecentErrors.Any())
                {
                    RecentErrors.Add(new SystemError
                    {
                        Timestamp = DateTime.Now,
                        ErrorType = "Info",
                        Path = "/",
                        Message = "System monitoring initialized"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading system performance metrics");
                // Initialize with default values
                CpuUsage = 0.3;
                MemoryUsage = 0.4;
                DatabaseConnections = 5;
                AverageResponseTime = 150;
            }
        }

        private async Task<int> GetRequestsPerMinute(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                  FROM [dbo].[SYSTEM_METRICS]
                  WHERE [Timestamp] >= DATEADD(MINUTE, -1, GETUTCDATE())");
        }

        private async Task<int> GetActiveSessions(SqlConnection connection)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) 
                  FROM sys.dm_exec_sessions 
                  WHERE is_user_process = 1 
                  AND last_request_end_time >= DATEADD(MINUTE, -5, GETUTCDATE())");
        }
    }

    // Other model classes should also be public
    public class CompletionData
    {
        public int CompletedCount { get; set; }
        public int TotalCount { get; set; }
        public double AverageProgress { get; set; }
    }

    public class EnrollmentData
    {
        public required string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class CategoryData
    {
        public required string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class TopCourseViewModel
    {
        public int CourseId { get; set; }
        public required string Title { get; set; }
        public required string InstructorName { get; set; }
        public int Enrollments { get; set; }
        public double CompletionRate { get; set; }
        public required string ThumbnailUrl { get; set; }
        public double Rating { get; set; }
    }

    public class DailyActivity
    {
        public DateTime Date { get; set; }
        public int UserCount { get; set; }
    }

    public class RoleDistribution
    {
        public required string Role { get; set; }
        public int Count { get; set; }
    }

    public class CompletionTrend
    {
        public required string Month { get; set; }
        public int CompletionCount { get; set; }
    }

    public class RatingDistribution
    {
        public decimal Rating { get; set; }
        public int Count { get; set; }
    }

    public class CategoryDistribution
    {
        public required string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class SystemError
    {
        public DateTime Timestamp { get; set; }
        public required string ErrorType { get; set; }
        public required string Path { get; set; }
        public required string Message { get; set; }
    }

    public class ResponseTimeData
    {
        public required string TimeLabel { get; set; }
        public int ResponseTime { get; set; }
    }

    public class ErrorRateData
    {
        public required string TimeLabel { get; set; }
        public double ErrorRate { get; set; }
    }
}