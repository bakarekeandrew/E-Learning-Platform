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
    public class InfoUpdateService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;

        public InfoUpdateService(IServiceProvider serviceProvider)
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
                    var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<InfoHub>>();

                    try
                    {
                        await UpdateActiveUsers(hubContext, stoppingToken);
                        await UpdateActiveCourses(hubContext, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue running
                        Debug.WriteLine($"Error in InfoUpdateService: {ex.Message}");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task UpdateActiveUsers(IHubContext<InfoHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            // Get all active users
            var activeUsers = (await connection.QueryAsync<(int UserId, DateTime LastActive)>(
                @"SELECT USER_ID, MAX(LAST_ACCESSED) as LastActive
                  FROM USER_PROGRESS
                  WHERE LAST_ACCESSED >= DATEADD(MINUTE, -5, GETDATE())
                  GROUP BY USER_ID")
            ).AsList();

            foreach (var user in activeUsers)
            {
                // Get user details
                var userInfo = await connection.QueryFirstOrDefaultAsync<UserInfoUpdate>(
                    @"SELECT 
                        u.USER_ID as UserId,
                        u.FULL_NAME as FullName,
                        u.EMAIL as Email,
                        CASE u.ROLE_ID 
                            WHEN 1 THEN 'Admin'
                            WHEN 2 THEN 'Instructor'
                            WHEN 3 THEN 'Student'
                            ELSE 'Unknown'
                        END as Role,
                        @LastActive as LastActive,
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE USER_ID = u.USER_ID) as EnrolledCourses,
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE USER_ID = u.USER_ID AND STATUS = 'Completed') as CompletedCourses,
                        ISNULL((SELECT AVG(CAST(PERCENT_COMPLETE as FLOAT)) FROM USER_PROGRESS WHERE USER_ID = u.USER_ID), 0) as AverageProgress
                    FROM USERS u
                    WHERE u.USER_ID = @UserId",
                    new { user.UserId, user.LastActive });

                if (userInfo != null)
                {
                    // Get recent activities
                    userInfo.RecentActivities = (await connection.QueryAsync<RecentActivity>(
                        @"SELECT TOP 5
                            CASE 
                                WHEN ump.STATUS IS NOT NULL THEN 'Module Progress'
                                WHEN asub.STATUS IS NOT NULL THEN 'Assignment'
                                ELSE 'Course Progress'
                            END as ActivityType,
                            COALESCE(m.TITLE, a.TITLE, 'Unknown') as Description,
                            COALESCE(ump.COMPLETED_ON, asub.SUBMITTED_ON, up.LAST_ACCESSED) as Timestamp
                        FROM USER_PROGRESS up
                        LEFT JOIN USER_MODULE_PROGRESS ump ON up.USER_ID = ump.USER_ID
                        LEFT JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                        LEFT JOIN ASSIGNMENT_SUBMISSIONS asub ON up.USER_ID = asub.USER_ID
                        LEFT JOIN ASSIGNMENTS a ON asub.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                        WHERE up.USER_ID = @UserId
                        ORDER BY COALESCE(ump.COMPLETED_ON, asub.SUBMITTED_ON, up.LAST_ACCESSED) DESC",
                        new { user.UserId })).AsList();

                    await hubContext.Clients.Group($"user_{user.UserId}")
                        .SendAsync("UserInfoUpdated", userInfo, cancellationToken: stoppingToken);

                    // Get and send user progress
                    var progressUpdate = new UserProgressUpdate
                    {
                        UserId = user.UserId.ToString(),
                        Courses = (await connection.QueryAsync<CourseProgress>(
                            @"SELECT 
                                c.TITLE as CourseName,
                                up.PERCENT_COMPLETE as Progress,
                                up.LAST_ACCESSED as LastAccessed,
                                ce.STATUS as Status
                            FROM USER_PROGRESS up
                            JOIN MODULES m ON up.MODULE_ID = m.MODULE_ID
                            JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                            JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND up.USER_ID = ce.USER_ID
                            WHERE up.USER_ID = @UserId",
                            new { user.UserId })).AsList(),
                        RecentAssessments = (await connection.QueryAsync<E_Learning_Platform.Hubs.AssessmentResult>(
                            @"SELECT TOP 5
                                a.TITLE as AssessmentName,
                                asub.GRADE as Score,
                                asub.SUBMITTED_ON as CompletionDate
                            FROM ASSIGNMENT_SUBMISSIONS asub
                            JOIN ASSIGNMENTS a ON asub.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                            WHERE asub.USER_ID = @UserId
                            ORDER BY asub.SUBMITTED_ON DESC",
                            new { user.UserId })).AsList()
                    };

                    await hubContext.Clients.Group($"user_{user.UserId}")
                        .SendAsync("UserProgressUpdated", progressUpdate, cancellationToken: stoppingToken);
                }
            }
        }

        private async Task UpdateActiveCourses(IHubContext<InfoHub> hubContext, CancellationToken stoppingToken)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(stoppingToken);

            // Get active courses (courses with recent activity)
            var activeCourses = (await connection.QueryAsync<int>(
                @"SELECT DISTINCT c.COURSE_ID
                  FROM COURSES c
                  JOIN MODULES m ON c.COURSE_ID = m.COURSE_ID
                  JOIN USER_PROGRESS up ON m.MODULE_ID = up.MODULE_ID
                  WHERE up.LAST_ACCESSED >= DATEADD(MINUTE, -5, GETDATE())")
            ).AsList();

            foreach (var courseId in activeCourses)
            {
                // Get course info
                var courseInfo = await connection.QueryFirstOrDefaultAsync<CourseInfoUpdate>(
                    @"SELECT 
                        c.COURSE_ID as CourseId,
                        c.TITLE as Title,
                        u.FULL_NAME as Instructor,
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE COURSE_ID = c.COURSE_ID) as EnrolledStudents,
                        ISNULL((SELECT CAST(COUNT(*) as FLOAT) / NULLIF(COUNT(*), 0)
                         FROM COURSE_ENROLLMENTS 
                         WHERE COURSE_ID = c.COURSE_ID AND STATUS = 'Completed'), 0) as CompletionRate,
                        ISNULL((SELECT AVG(CAST(RATING as FLOAT)) FROM REVIEWS WHERE COURSE_ID = c.COURSE_ID), 0) as AverageRating,
                        (SELECT COUNT(DISTINCT up.USER_ID) 
                         FROM USER_PROGRESS up
                         JOIN MODULES m ON up.MODULE_ID = m.MODULE_ID
                         WHERE m.COURSE_ID = c.COURSE_ID 
                         AND up.LAST_ACCESSED >= DATEADD(HOUR, -1, GETDATE())) as ActiveStudents
                    FROM COURSES c
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    WHERE c.COURSE_ID = @CourseId",
                    new { CourseId = courseId });

                if (courseInfo != null)
                {
                    // Get module status
                    courseInfo.Modules = (await connection.QueryAsync<ModuleStatus>(
                        @"SELECT 
                            m.TITLE as ModuleName,
                            COUNT(DISTINCT ump.USER_ID) as TotalStudents,
                            COUNT(DISTINCT CASE WHEN ump.STATUS = 'Completed' THEN ump.USER_ID END) as CompletedStudents,
                            ISNULL(AVG(CAST(asub.GRADE as FLOAT)), 0) as AverageScore
                        FROM MODULES m
                        LEFT JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID
                        LEFT JOIN ASSIGNMENTS a ON m.COURSE_ID = a.COURSE_ID
                        LEFT JOIN ASSIGNMENT_SUBMISSIONS asub ON a.ASSIGNMENT_ID = asub.ASSIGNMENT_ID
                        WHERE m.COURSE_ID = @CourseId
                        GROUP BY m.TITLE",
                        new { CourseId = courseId })).AsList();

                    await hubContext.Clients.Group($"course_{courseId}")
                        .SendAsync("CourseInfoUpdated", courseInfo, cancellationToken: stoppingToken);

                    // Get and send course progress
                    var progressUpdate = new CourseProgressUpdate
                    {
                        CourseId = courseId.ToString(),
                        StudentProgress = (await connection.QueryAsync<StudentProgress>(
                            @"SELECT 
                                u.FULL_NAME as StudentName,
                                up.PERCENT_COMPLETE as Progress,
                                up.LAST_ACCESSED as LastActive,
                                m.TITLE as CurrentModule
                            FROM USER_PROGRESS up
                            JOIN USERS u ON up.USER_ID = u.USER_ID
                            JOIN MODULES m ON up.MODULE_ID = m.MODULE_ID
                            WHERE m.COURSE_ID = @CourseId
                            ORDER BY up.LAST_ACCESSED DESC",
                            new { CourseId = courseId })).AsList()
                    };

                    await hubContext.Clients.Group($"course_{courseId}")
                        .SendAsync("CourseProgressUpdated", progressUpdate, cancellationToken: stoppingToken);
                }
            }
        }
    }
} 