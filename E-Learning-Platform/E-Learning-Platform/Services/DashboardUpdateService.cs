using Microsoft.AspNetCore.SignalR;
using E_Learning_Platform.Hubs;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Dapper;

namespace E_Learning_Platform.Services
{
    public class DashboardUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Random _random = new Random();
        private readonly string _connectionString;

        public DashboardUpdateService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DashboardHub>>();

                    try
                    {
                        // Update system metrics
                        await UpdateSystemMetrics(hubContext, stoppingToken);

                        // Update user activity
                        await UpdateUserActivity(hubContext, stoppingToken);

                        // Update course analytics
                        await UpdateCourseAnalytics(hubContext, stoppingToken);

                        // Update error metrics
                        await UpdateErrorMetrics(hubContext, stoppingToken);

                        // New updates
                        await UpdateStudentEngagement(hubContext, stoppingToken);
                        await UpdateLearningProgress(hubContext, stoppingToken);
                        await UpdateResourceUtilization(hubContext, stoppingToken);

                        // Send periodic notifications
                        await SendPeriodicNotifications(hubContext, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // Log error and send notification
                        await hubContext.Clients.Group("analytics")
                            .SendAsync("ReceiveNotification", 
                                     $"Error updating analytics: {ex.Message}", 
                                     "danger", 
                                     cancellationToken: stoppingToken);
                    }
                }

                // Wait for 5 seconds before next update
                await Task.Delay(5000, stoppingToken);
            }
        }

        private async Task UpdateSystemMetrics(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var process = Process.GetCurrentProcess();
            
            // Get CPU usage
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;
            await Task.Delay(100, stoppingToken); // Sample for 100ms
            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            // Get memory usage
            var workingSet = process.WorkingSet64;
            var totalMemory = process.PrivateMemorySize64;
            var memoryUsage = (double)workingSet / totalMemory;

            // Get database metrics
            int dbConnections;
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(stoppingToken);
                dbConnections = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sys.dm_exec_connections WHERE session_id > 50",
                    commandTimeout: 5);
            }

            var metrics = new SystemMetricsUpdate
            {
                CpuUsage = Math.Min(1, Math.Max(0, cpuUsageTotal)),
                MemoryUsage = Math.Min(1, Math.Max(0, memoryUsage)),
                DatabaseConnections = dbConnections,
                ResponseTime = _random.Next(50, 200) // Simulated response time
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateSystemMetrics", metrics, cancellationToken: stoppingToken);
        }

        private async Task UpdateUserActivity(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            var activityData = await connection.QueryAsync<(DateTime Date, int Count)>(
                @"SELECT 
                    CAST(LAST_ACCESSED AS DATE) as Date,
                    COUNT(DISTINCT USER_ID) as Count
                FROM COURSE_PROGRESS
                WHERE LAST_ACCESSED >= DATEADD(DAY, -7, GETDATE())
                GROUP BY CAST(LAST_ACCESSED AS DATE)
                ORDER BY Date");

            var update = new UserActivityUpdate
            {
                Labels = activityData.Select(d => d.Date.ToString("MMM dd")).ToList(),
                ActivityData = activityData.Select(d => d.Count).ToList()
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateUserActivity", update, cancellationToken: stoppingToken);
        }

        private async Task UpdateCourseAnalytics(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            var completionData = await connection.QueryAsync<(string Month, int Count)>(
                @"WITH Months AS (
                    SELECT TOP 6
                        DATEADD(MONTH, -number, GETDATE()) AS MonthDate
                    FROM master.dbo.spt_values
                    WHERE type = 'P'
                    ORDER BY number
                )
                SELECT 
                    FORMAT(m.MonthDate, 'MMM yyyy') AS Month,
                    COUNT(DISTINCT ce.ENROLLMENT_ID) AS Count
                FROM Months m
                LEFT JOIN COURSE_ENROLLMENTS ce ON 
                    FORMAT(ce.COMPLETION_DATE, 'yyyyMM') = FORMAT(m.MonthDate, 'yyyyMM')
                    AND ce.STATUS = 'Completed'
                GROUP BY m.MonthDate, FORMAT(m.MonthDate, 'MMM yyyy')
                ORDER BY m.MonthDate");

            var update = new CourseAnalyticsUpdate
            {
                Labels = completionData.Select(d => d.Month).ToList(),
                CompletionData = completionData.Select(d => (double)d.Count).ToList()
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateCourseProgress", update, cancellationToken: stoppingToken);
        }

        private async Task UpdateErrorMetrics(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            var errorData = await connection.QueryAsync<(string TimeLabel, double ErrorRate)>(
                @"SELECT 
                    FORMAT([Timestamp], 'HH:mm') as TimeLabel,
                    [ErrorRate]
                FROM [dbo].[ERROR_METRICS]
                WHERE [Timestamp] >= DATEADD(HOUR, -24, GETDATE())
                ORDER BY [Timestamp]");

            var update = new ErrorMetricsUpdate
            {
                Labels = errorData.Select(d => d.TimeLabel).ToList(),
                ErrorRateData = errorData.Select(d => d.ErrorRate).ToList()
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateErrorRate", update, cancellationToken: stoppingToken);
        }

        private async Task UpdateStudentEngagement(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            // Get hourly active students
            var timeSlotData = await connection.QueryAsync<(string TimeSlot, int Count)>(
                @"SELECT 
                    FORMAT(LAST_ACCESSED, 'HH:00') as TimeSlot,
                    COUNT(DISTINCT USER_ID) as Count
                FROM COURSE_PROGRESS
                WHERE LAST_ACCESSED >= DATEADD(HOUR, -24, GETDATE())
                GROUP BY FORMAT(LAST_ACCESSED, 'HH:00')
                ORDER BY TimeSlot");

            // Update student engagement metrics
            var studentEngagementUpdate = new StudentEngagementUpdate
            {
                TimeSlots = timeSlotData.Select(d => d.TimeSlot).ToList(),
                ActiveStudents = timeSlotData.Select(d => d.Count).ToList(),
                ResourceAccess = (await connection.QueryAsync<(string ResourceType, int Count)>(
                    @"SELECT 
                        m.TITLE as ResourceType,
                        COUNT(*) as AccessCount
                    FROM USER_PROGRESS up
                    JOIN MODULES m ON up.MODULE_ID = m.MODULE_ID
                    WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(r => r.ResourceType, r => r.Count),
                TopEngaged = (await connection.QueryAsync<TopEngagementMetric>(
                    @"WITH StudentEngagement AS (
                        SELECT 
                            u.FULL_NAME as StudentName,
                            COUNT(DISTINCT up.MODULE_ID) as ResourcesAccessed,
                            SUM(DATEDIFF(MINUTE, up.LAST_ACCESSED, DATEADD(MINUTE, m.DURATION_MINUTES, up.LAST_ACCESSED))) as MinutesEngaged,
                            (SELECT CAST(COUNT(*) AS FLOAT) / NULLIF(COUNT(*), 0)
                             FROM COURSE_ENROLLMENTS ce
                             WHERE ce.USER_ID = u.USER_ID AND ce.STATUS = 'Completed') as CompletionRate
                        FROM USERS u
                        JOIN USER_PROGRESS up ON u.USER_ID = up.USER_ID
                        JOIN MODULES m ON up.MODULE_ID = m.MODULE_ID
                        WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                        GROUP BY u.USER_ID, u.FULL_NAME
                    )
                    SELECT TOP 5 *
                    FROM StudentEngagement
                    ORDER BY MinutesEngaged DESC")).AsList()
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateStudentEngagement", studentEngagementUpdate, cancellationToken: stoppingToken);

            // Update resource utilization
            var resourceUpdate = new ResourceUtilizationUpdate
            {
                ResourceViews = (await connection.QueryAsync<(string Resource, int Views)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        COUNT(*) as ViewCount
                    FROM MODULES m
                    JOIN USER_PROGRESS up ON m.MODULE_ID = up.MODULE_ID
                    WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(r => r.Resource, r => r.Views),
                AverageTimeSpent = (await connection.QueryAsync<(string Resource, double Minutes)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        AVG(m.DURATION_MINUTES) as AvgMinutes
                    FROM MODULES m
                    JOIN USER_PROGRESS up ON m.MODULE_ID = up.MODULE_ID
                    WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(t => t.Resource, t => t.Minutes),
                PopularTimeSlots = (await connection.QueryAsync<string>(
                    @"SELECT TOP 5
                        FORMAT(LAST_ACCESSED, 'HH:00') as TimeSlot
                    FROM USER_PROGRESS
                    WHERE LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY FORMAT(LAST_ACCESSED, 'HH:00')
                    ORDER BY COUNT(*) DESC")).ToList(),
                ResourceEffectiveness = (await connection.QueryAsync<(string Resource, double Rate)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        AVG(CASE WHEN ump.STATUS = 'Completed' THEN 1.0 ELSE 0.0 END) as CompletionRate
                    FROM MODULES m
                    JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID
                    WHERE ump.COMPLETED_ON >= DATEADD(DAY, -7, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(e => e.Resource, e => e.Rate)
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateResourceUtilization", resourceUpdate, cancellationToken: stoppingToken);
        }

        private async Task UpdateLearningProgress(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            // Get module progress using the correct table name and columns
            var moduleProgress = await connection.QueryAsync<(string Module, int CompletionCount)>(
                @"SELECT 
                    m.TITLE as Module,
                    COUNT(DISTINCT ump.USER_ID) as CompletionCount
                FROM MODULES m
                LEFT JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID
                WHERE ump.STATUS = 'completed' 
                    AND ump.COMPLETED_ON >= DATEADD(DAY, -7, GETDATE())
                GROUP BY m.TITLE");

            // Get recent assessment data from ASSIGNMENT_SUBMISSIONS
            var recentAssessments = await connection.QueryAsync<AssessmentMetric>(
                @"SELECT 
                    a.TITLE as ASSESSMENT_NAME,
                    AVG(CAST(asub.GRADE as FLOAT)) as AverageScore,
                    COUNT(DISTINCT asub.USER_ID) as Participants,
                    MAX(asub.SUBMITTED_ON) as CompletionDate
                FROM ASSIGNMENTS a
                JOIN ASSIGNMENT_SUBMISSIONS asub ON a.ASSIGNMENT_ID = asub.ASSIGNMENT_ID
                WHERE asub.SUBMITTED_ON >= DATEADD(DAY, -7, GETDATE())
                GROUP BY a.TITLE");

            // Calculate skill progress based on module types/topics
            var skillProgress = await connection.QueryAsync<(string Skill, double Progress)>(
                @"SELECT 
                    COALESCE(m.TITLE, 'General') as SKILL_NAME,
                    AVG(CASE 
                        WHEN ump.STATUS = 'completed' THEN 100.0 
                        WHEN ump.STATUS = 'in_progress' THEN 50.0
                        ELSE 0.0 
                    END) as Progress
                FROM MODULES m
                LEFT JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID
                WHERE ump.COMPLETED_ON >= DATEADD(DAY, -7, GETDATE()) 
                    OR ump.STATUS = 'in_progress'
                GROUP BY m.TITLE");

            var update = new LearningProgressUpdate
            {
                ModuleProgress = moduleProgress.ToDictionary(m => m.Module, m => m.CompletionCount),
                RecentAssessments = recentAssessments.AsList(),
                SkillProgress = skillProgress.ToDictionary(s => s.Skill, s => s.Progress)
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateLearningProgress", update, cancellationToken: stoppingToken);
        }

        private async Task UpdateResourceUtilization(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            var resourceUpdate = new ResourceUtilizationUpdate
            {
                ResourceViews = (await connection.QueryAsync<(string Resource, int Views)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        COUNT(*) as ViewCount
                    FROM MODULES m
                    JOIN USER_PROGRESS up ON m.MODULE_ID = up.MODULE_ID
                    WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(r => r.Resource, r => r.Views),
                AverageTimeSpent = (await connection.QueryAsync<(string Resource, double Minutes)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        AVG(m.DURATION_MINUTES) as AvgMinutes
                    FROM MODULES m
                    JOIN USER_PROGRESS up ON m.MODULE_ID = up.MODULE_ID
                    WHERE up.LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(t => t.Resource, t => t.Minutes),
                PopularTimeSlots = (await connection.QueryAsync<string>(
                    @"SELECT TOP 5
                        FORMAT(LAST_ACCESSED, 'HH:00') as TimeSlot
                    FROM USER_PROGRESS
                    WHERE LAST_ACCESSED >= DATEADD(DAY, -1, GETDATE())
                    GROUP BY FORMAT(LAST_ACCESSED, 'HH:00')
                    ORDER BY COUNT(*) DESC")).ToList(),
                ResourceEffectiveness = (await connection.QueryAsync<(string Resource, double Rate)>(
                    @"SELECT 
                        m.TITLE as ResourceName,
                        AVG(CASE WHEN ump.STATUS = 'Completed' THEN 1.0 ELSE 0.0 END) as CompletionRate
                    FROM MODULES m
                    JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID
                    WHERE ump.COMPLETED_ON >= DATEADD(DAY, -7, GETDATE())
                    GROUP BY m.TITLE")).ToDictionary(e => e.Resource, e => e.Rate)
            };

            await hubContext.Clients.Group("analytics")
                .SendAsync("UpdateResourceUtilization", resourceUpdate, cancellationToken: stoppingToken);
        }

        private async Task SendPeriodicNotifications(IHubContext<DashboardHub> hubContext, CancellationToken stoppingToken)
        {
            // Send periodic notifications (30% chance)
            if (_random.Next(0, 100) < 30)
            {
                var notifications = new List<(string message, string type)>
                {
                    ("System performance metrics updated", "info"),
                    ("New course completion milestone reached", "success"),
                    ("Increased user activity detected", "info"),
                    ("Error rate spike detected", "warning"),
                    ("Database optimization recommended", "warning")
                };

                var notification = notifications[_random.Next(notifications.Count)];
                await hubContext.Clients.Group("analytics")
                    .SendAsync("ReceiveNotification", 
                             notification.message, 
                             notification.type, 
                             cancellationToken: stoppingToken);
            }
        }
    }
} 