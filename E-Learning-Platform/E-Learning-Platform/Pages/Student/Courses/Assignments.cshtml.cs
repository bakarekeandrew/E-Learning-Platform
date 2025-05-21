using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Student.Courses
{
    [Authorize(Policy = "StudentOnly")]
    public class AssignmentsModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<AssignmentsModel> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AssignmentsModel(
            ILogger<AssignmentsModel> logger,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;Initial Catalog=ONLINE_LEARNING_PLATFORM;Integrated Security=True;TrustServerCertificate=True";
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        [FromRoute]
        public int? CourseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Filter { get; set; } = "all";

        public string CourseTitle { get; set; }
        public string ErrorMessage { get; set; }
        public int CurrentUserId { get; set; }
        public List<AssignmentViewModel> Assignments { get; set; }
        public bool HasPendingAssignments => PendingAssignments > 0;
        public int PendingAssignments { get; set; }

        // Get current user ID from claims or session
        private int GetCurrentUserId()
        {
            // First try to get from claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("UserId")?.Value;

            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }

            // Fallback to session
            if (_httpContextAccessor.HttpContext.Session.TryGetValue("UserId", out byte[] userIdBytes))
            {
                return BitConverter.ToInt32(userIdBytes);
            }

            // If we can't get the user ID, throw an exception - user should be logged in at this point
            throw new InvalidOperationException("User ID not found. User might not be properly authenticated.");
        }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                // Get current user ID from claims or session
                CurrentUserId = GetCurrentUserId();
                _logger.LogInformation($"Loading assignments for user ID: {CurrentUserId}");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First, let's check if we have any assignments at all to help with debugging
                var totalAssignments = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM ASSIGNMENTS");

                _logger.LogInformation($"Total assignments in database: {totalAssignments}");

                // If specific course is requested, verify user enrollment
                if (CourseId.HasValue)
                {
                    var courseData = await connection.QueryFirstOrDefaultAsync<CourseData>(@"
                        SELECT 
                            c.TITLE AS Title,
                            (SELECT COUNT(1) FROM COURSE_ENROLLMENTS 
                             WHERE USER_ID = @UserId AND COURSE_ID = @CourseId) AS IsEnrolled
                        FROM COURSES c
                        WHERE c.COURSE_ID = @CourseId",
                        new { UserId = CurrentUserId, CourseId });

                    if (courseData == null)
                    {
                        ErrorMessage = "Course not found.";
                        return Page();
                    }

                    if (courseData.IsEnrolled == 0)
                    {
                        ErrorMessage = "You are not enrolled in this course.";
                        return Page();
                    }

                    CourseTitle = courseData.Title;
                }

                // Modified query to check if we're getting enrollments
                var enrollmentCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId",
                    new { UserId = CurrentUserId });

                _logger.LogInformation($"User {CurrentUserId} has {enrollmentCount} course enrollments");

                // Build base query for assignments
                string assignmentQuery = @"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        a.COURSE_ID AS CourseId,
                        c.TITLE AS CourseTitle,
                        s.SUBMISSION_ID AS SubmissionId,
                        s.SUBMITTED_ON AS SubmittedOn,
                        s.GRADE AS Grade,
                        s.FEEDBACK AS Feedback,
                        s.STATUS AS Status
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @UserId
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId";

                // Add course filter if specified
                if (CourseId.HasValue)
                {
                    assignmentQuery += " WHERE a.COURSE_ID = @CourseId";
                }

                // Apply filter conditions
                string filterCondition = "";
                switch (Filter?.ToLower())
                {
                    case "pending":
                        filterCondition = CourseId.HasValue
                            ? " AND s.SUBMISSION_ID IS NULL"
                            : " WHERE s.SUBMISSION_ID IS NULL";
                        break;
                    case "submitted":
                        filterCondition = CourseId.HasValue
                            ? " AND s.SUBMISSION_ID IS NOT NULL"
                            : " WHERE s.SUBMISSION_ID IS NOT NULL";
                        break;
                    case "graded":
                        filterCondition = CourseId.HasValue
                            ? " AND s.GRADE IS NOT NULL"
                            : " WHERE s.GRADE IS NOT NULL";
                        break;
                    case "upcoming":
                        filterCondition = CourseId.HasValue
                            ? " AND a.DUE_DATE > GETDATE() AND a.DUE_DATE <= DATEADD(day, 7, GETDATE()) AND s.SUBMISSION_ID IS NULL"
                            : " WHERE a.DUE_DATE > GETDATE() AND a.DUE_DATE <= DATEADD(day, 7, GETDATE()) AND s.SUBMISSION_ID IS NULL";
                        break;
                    case "overdue":
                        filterCondition = CourseId.HasValue
                            ? " AND a.DUE_DATE < GETDATE() AND s.SUBMISSION_ID IS NULL"
                            : " WHERE a.DUE_DATE < GETDATE() AND s.SUBMISSION_ID IS NULL";
                        break;
                }

                assignmentQuery += filterCondition;
                assignmentQuery += " ORDER BY a.DUE_DATE ASC";

                _logger.LogInformation($"Executing assignment query for user {CurrentUserId}");

                // Execute query
                var assignments = await connection.QueryAsync<AssignmentViewModel>(assignmentQuery,
                    new { UserId = CurrentUserId, CourseId });

                // Process assignments
                Assignments = assignments?.ToList() ?? new List<AssignmentViewModel>();
                _logger.LogInformation($"Found {Assignments.Count} assignments for user {CurrentUserId}");

                foreach (var assignment in Assignments)
                {
                    assignment.IsSubmitted = assignment.SubmissionId.HasValue;
                    assignment.IsGraded = assignment.Grade.HasValue;
                    assignment.IsOverdue = !assignment.IsSubmitted && assignment.DueDate < DateTime.Now;
                    assignment.IsDueSoon = !assignment.IsSubmitted && !assignment.IsOverdue &&
                                           assignment.DueDate <= DateTime.Now.AddDays(7);
                }

                // Count pending assignments
                PendingAssignments = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*)
                    FROM ASSIGNMENTS a
                    JOIN COURSE_ENROLLMENTS ce ON a.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @UserId
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId
                    WHERE s.SUBMISSION_ID IS NULL AND a.DUE_DATE > GETDATE()",
                    new { UserId = CurrentUserId });

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading assignments for user {UserId}", CurrentUserId);
                ErrorMessage = "An error occurred while loading assignments: " + ex.Message;
                return Page();
            }
        }

        public class AssignmentViewModel
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; }
            public string Instructions { get; set; }
            public DateTime DueDate { get; set; }
            public int MaxScore { get; set; }
            public int CourseId { get; set; }
            public string CourseTitle { get; set; }
            public int? SubmissionId { get; set; }
            public DateTime? SubmittedOn { get; set; }
            public decimal? Grade { get; set; }
            public string Feedback { get; set; }
            public string Status { get; set; }

            // Derived properties
            public bool IsSubmitted { get; set; }
            public bool IsGraded { get; set; }
            public bool IsOverdue { get; set; }
            public bool IsDueSoon { get; set; }
        }

        private class CourseData
        {
            public string Title { get; set; }
            public int IsEnrolled { get; set; }
        }
    }
}