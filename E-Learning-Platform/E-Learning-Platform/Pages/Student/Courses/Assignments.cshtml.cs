using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class AssignmentsModel : StudentPageModel
    {
        public AssignmentsModel(
            ILogger<AssignmentsModel> logger,
            IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        [FromRoute]
        public int? CourseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Filter { get; set; } = "all";

        public string CourseTitle { get; set; }
        public List<AssignmentViewModel> Assignments { get; set; }
        public bool HasPendingAssignments => PendingAssignments > 0;
        public int PendingAssignments { get; set; }
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                var studentId = GetStudentId();
                _logger.LogInformation("Loading assignments for student ID: {StudentId}", studentId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First, let's check if we have any assignments at all to help with debugging
                var totalAssignments = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM ASSIGNMENTS");

                _logger.LogInformation("Total assignments in database: {Count}", totalAssignments);

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
                        new { UserId = studentId, CourseId });

                    if (courseData == null)
                    {
                        ModelState.AddModelError("", "Course not found.");
                        return Page();
                    }

                    if (courseData.IsEnrolled == 0)
                    {
                        ModelState.AddModelError("", "You are not enrolled in this course.");
                        return Page();
                    }

                    CourseTitle = courseData.Title;
                }

                // Modified query to check if we're getting enrollments
                var enrollmentCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId",
                    new { UserId = studentId });

                _logger.LogInformation("Student {StudentId} has {Count} course enrollments", studentId, enrollmentCount);

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
                            ? " AND s.STATUS = 'Graded'"
                            : " WHERE s.STATUS = 'Graded'";
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

                _logger.LogInformation("Executing assignment query for student {StudentId}", studentId);

                // Execute query
                var assignments = await connection.QueryAsync<AssignmentViewModel>(
                    assignmentQuery,
                    new { UserId = studentId, CourseId });

                // Process assignments
                Assignments = assignments?.ToList() ?? new List<AssignmentViewModel>();
                _logger.LogInformation("Found {Count} assignments for student {StudentId}", Assignments.Count, studentId);

                foreach (var assignment in Assignments)
                {
                    assignment.IsSubmitted = assignment.SubmissionId.HasValue;
                    assignment.IsGraded = assignment.Status == "Graded";
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
                    new { UserId = studentId });

                return Page();
            }, "Error loading assignments");
        }

        public class AssignmentViewModel
        {
            public int AssignmentId { get; set; }
            public required string Title { get; set; }
            public required string Instructions { get; set; }
            public DateTime DueDate { get; set; }
            public int MaxScore { get; set; }
            public int CourseId { get; set; }
            public required string CourseTitle { get; set; }
            public int? SubmissionId { get; set; }
            public DateTime? SubmittedOn { get; set; }
            public decimal? Grade { get; set; }
            public string Feedback { get; set; } = string.Empty;
            public required string Status { get; set; }

            // Derived properties
            public bool IsSubmitted { get; set; }
            public bool IsGraded { get; set; }
            public bool IsOverdue { get; set; }
            public bool IsDueSoon { get; set; }
        }

        private class CourseData
        {
            public required string Title { get; set; }
            public int IsEnrolled { get; set; }
        }
    }
}