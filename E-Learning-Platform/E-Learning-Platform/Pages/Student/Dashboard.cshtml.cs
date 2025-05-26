using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Student
{
    public class DashboardModel : StudentPageModel
    {
        public DashboardModel(
            ILogger<DashboardModel> logger,
            IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        public DashboardStats Stats { get; set; } = new DashboardStats();
        public List<EnrolledCourse> MyCourses { get; set; } = new List<EnrolledCourse>();
        public List<UpcomingAssignment> UpcomingAssignments { get; set; } = new List<UpcomingAssignment>();
        public List<RecentActivity> RecentActivities { get; set; } = new List<RecentActivity>();

        public class DashboardStats
        {
            public int EnrolledCourses { get; set; }
            public int CompletedAssignments { get; set; }
            public int PendingAssignments { get; set; }
            public decimal OverallProgress { get; set; }
        }

        public class EnrolledCourse
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Instructor { get; set; }
            public decimal Progress { get; set; }
            public DateTime EnrollmentDate { get; set; }
            public DateTime? CompletionDate { get; set; }
        }

        public class UpcomingAssignment
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; }
            public string CourseTitle { get; set; }
            public DateTime DueDate { get; set; }
            public bool IsSubmitted { get; set; }
        }

        public class RecentActivity
        {
            public string Type { get; set; }
            public string Description { get; set; }
            public DateTime Timestamp { get; set; }
            public string CourseTitle { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                var studentId = GetStudentId();
                _logger.LogInformation("Loading dashboard for student ID: {StudentId}", studentId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Load dashboard statistics
                Stats = await connection.QueryFirstOrDefaultAsync<DashboardStats>(@"
                    SELECT 
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId) AS EnrolledCourses,
                        (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE USER_ID = @UserId AND STATUS = 'Graded') AS CompletedAssignments,
                        (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE USER_ID = @UserId AND STATUS = 'Submitted') AS PendingAssignments,
                        COALESCE((SELECT AVG(CAST(PROGRESS AS DECIMAL(5,2))) FROM COURSE_PROGRESS WHERE USER_ID = @UserId), 0) AS OverallProgress",
                    new { UserId = studentId }) ?? new DashboardStats();

                // Load enrolled courses
                MyCourses = (await connection.QueryAsync<EnrolledCourse>(@"
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        u.FULL_NAME AS Instructor,
                        COALESCE(cp.PROGRESS, 0) AS Progress,
                        ce.ENROLLMENT_DATE AS EnrollmentDate,
                        ce.COMPLETION_DATE AS CompletionDate
                    FROM COURSE_ENROLLMENTS ce
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    LEFT JOIN COURSE_PROGRESS cp ON ce.COURSE_ID = cp.COURSE_ID AND ce.USER_ID = cp.USER_ID
                    WHERE ce.USER_ID = @UserId
                    ORDER BY ce.ENROLLMENT_DATE DESC",
                    new { UserId = studentId })).ToList();

                // Load upcoming assignments
                UpcomingAssignments = (await connection.QueryAsync<UpcomingAssignment>(@"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        c.TITLE AS CourseTitle,
                        a.DUE_DATE AS DueDate,
                        CASE WHEN s.SUBMISSION_ID IS NOT NULL THEN 1 ELSE 0 END AS IsSubmitted
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId
                    WHERE ce.USER_ID = @UserId
                    AND a.DUE_DATE >= GETDATE()
                    ORDER BY a.DUE_DATE ASC",
                    new { UserId = studentId })).ToList();

                // Load recent activities
                RecentActivities = (await connection.QueryAsync<RecentActivity>(@"
                    SELECT TOP 10
                        'Assignment Due' AS Type,
                        c.TITLE + ' - ' + a.TITLE AS Description,
                        a.DUE_DATE AS Timestamp,
                        c.TITLE AS CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                    WHERE ce.USER_ID = @UserId AND a.DUE_DATE >= GETDATE()
                    UNION ALL
                    SELECT 
                        'Submission' AS Type,
                        c.TITLE + ' - ' + a.TITLE + ' - ' + s.STATUS AS Description,
                        s.SUBMITTED_ON AS Timestamp,
                        c.TITLE AS CourseTitle
                    FROM ASSIGNMENT_SUBMISSIONS s
                    JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE s.USER_ID = @UserId
                    ORDER BY Timestamp DESC",
                    new { UserId = studentId })).ToList();

                return Page();
            }, "Error loading student dashboard");
        }
    }
}