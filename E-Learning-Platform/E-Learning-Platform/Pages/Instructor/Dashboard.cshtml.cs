using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Logging;
using E_Learning_Platform.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Instructor
{
    [Authorize(Roles = "INSTRUCTOR")]
    public class DashboardModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<DashboardModel> _logger;

        public DashboardModel(
            IConfiguration configuration, 
            ILogger<DashboardModel> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        public DashboardStats Stats { get; set; } = new DashboardStats();
        public List<Course> MyCourses { get; set; } = new List<Course>();
        public List<RecentActivity> RecentActivities { get; set; } = new List<RecentActivity>();
        public List<StudentSubmission> PendingSubmissions { get; set; } = new List<StudentSubmission>();
        public string ErrorMessage { get; set; }

        public class DashboardStats
        {
            public int CourseCount { get; set; }
            public int StudentCount { get; set; }
            public int PendingAssignments { get; set; }
            public decimal MonthlyEarnings { get; set; } = 0;
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public int StudentCount { get; set; }
            public decimal Rating { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedDate { get; set; }
        }

        public class RecentActivity
        {
            public string Type { get; set; }
            public string Description { get; set; }
            public DateTime Timestamp { get; set; }
            public string CourseTitle { get; set; }
        }

        public class StudentSubmission
        {
            public int SubmissionId { get; set; }
            public string StudentName { get; set; }
            public string AssignmentTitle { get; set; }
            public string CourseTitle { get; set; }
            public DateTime SubmittedDate { get; set; }
        }

        private async Task<int> GetCurrentUserIdAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("User ID claim not found or invalid");
                throw new InvalidOperationException("User not properly authenticated");
            }
            return userId;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                var userId = await GetCurrentUserIdAsync();
                _logger.LogInformation("Loading dashboard for instructor {UserId}", userId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Database connection opened successfully");

                // Load data in separate try-catch blocks to identify which query might be failing
                try
                {
                    // Load dashboard statistics
                    Stats = await connection.QueryFirstOrDefaultAsync<DashboardStats>(@"
                        SELECT 
                            (SELECT COUNT(*) FROM COURSES WHERE CREATED_BY = @InstructorId) AS CourseCount,
                            (SELECT COUNT(DISTINCT e.USER_ID) 
                            FROM COURSE_ENROLLMENTS e
                             JOIN COURSES c ON e.COURSE_ID = c.COURSE_ID
                             WHERE c.CREATED_BY = @InstructorId) AS StudentCount,
                            (SELECT COUNT(*) 
                             FROM ASSIGNMENT_SUBMISSIONS s
                             JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                             JOIN MODULES m ON a.MODULE_ID = m.MODULE_ID
                             JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                             WHERE c.CREATED_BY = @InstructorId AND s.GRADE IS NULL) AS PendingAssignments,
                            0 AS MonthlyEarnings",
                        new { InstructorId = userId });
                    _logger.LogInformation("Stats loaded successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading stats");
                    Stats = new DashboardStats(); // Use default empty stats
                }

                try
                {
                    // Load instructor's courses
                    MyCourses = (await connection.QueryAsync<Course>(@"
                        SELECT 
                            c.COURSE_ID AS CourseId,
                            c.TITLE AS Title,
                            (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE COURSE_ID = c.COURSE_ID) AS StudentCount,
                            ISNULL((SELECT AVG(RATING) FROM REVIEWS WHERE COURSE_ID = c.COURSE_ID), 0) AS Rating,
                            c.IS_ACTIVE AS IsActive,
                            c.CREATION_DATE AS CreatedDate
                        FROM COURSES c
                        WHERE c.CREATED_BY = @InstructorId
                        ORDER BY c.CREATION_DATE DESC",
                        new { InstructorId = userId })).ToList();
                    _logger.LogInformation("Courses loaded successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading courses");
                    MyCourses = new List<Course>(); // Use empty list
                }

                try
                {
                    // Load pending submissions with simplified query
                    PendingSubmissions = (await connection.QueryAsync<StudentSubmission>(@"
                        SELECT 
                            s.SUBMISSION_ID AS SubmissionId,
                            u.FULL_NAME AS StudentName,
                            a.TITLE AS AssignmentTitle,
                            c.TITLE AS CourseTitle,
                            s.SUBMITTED_ON AS SubmittedDate
                        FROM ASSIGNMENT_SUBMISSIONS s
                        JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                        JOIN MODULES m ON a.MODULE_ID = m.MODULE_ID
                        JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                        JOIN USERS u ON s.USER_ID = u.USER_ID
                        WHERE c.CREATED_BY = @InstructorId AND s.GRADE IS NULL
                        ORDER BY s.SUBMITTED_ON DESC",
                        new { InstructorId = userId })).ToList();
                    _logger.LogInformation("Pending submissions loaded successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading pending submissions");
                    PendingSubmissions = new List<StudentSubmission>(); // Use empty list
                }

                try
                {
                    // Load recent activities with simplified query
                    RecentActivities = (await connection.QueryAsync<RecentActivity>(@"
                        SELECT TOP 5
                            'New Enrollment' AS Type,
                            u.FULL_NAME + ' enrolled in ' + c.TITLE AS Description,
                            e.ENROLLMENT_DATE AS Timestamp,
                            c.TITLE AS CourseTitle
                        FROM COURSE_ENROLLMENTS e
                        JOIN COURSES c ON e.COURSE_ID = c.COURSE_ID
                        JOIN USERS u ON e.USER_ID = u.USER_ID
                        WHERE c.CREATED_BY = @InstructorId
                        ORDER BY e.ENROLLMENT_DATE DESC",
                        new { InstructorId = userId })).ToList();
                    _logger.LogInformation("Recent activities loaded successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading recent activities");
                    RecentActivities = new List<RecentActivity>(); // Use empty list
                }

                return Page();
            }
            catch (InvalidOperationException)
            {
                return RedirectToPage("/Login");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL Error occurred while loading dashboard");
                ErrorMessage = "Database error occurred. Please try again later.";
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while loading dashboard");
                ErrorMessage = "An unexpected error occurred. Please try again later.";
                return Page();
            }
        }
    }
}