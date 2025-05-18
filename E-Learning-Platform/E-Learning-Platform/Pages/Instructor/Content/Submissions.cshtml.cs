using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class SubmissionsModel : PageModel
    {
        private readonly string _connectionString;

        public SubmissionsModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        [BindProperty(SupportsGet = true)]
        public int AssignmentId { get; set; }

        public Assignment AssignmentDetails { get; set; } = new Assignment();
        public List<SubmissionData> Submissions { get; set; } = new List<SubmissionData>();
        public bool AssignmentExists { get; set; } = false;

        public class Assignment
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Instructions { get; set; } = string.Empty;
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; } = 100;
            public string CourseTitle { get; set; } = string.Empty;
            public int CourseId { get; set; }
            // Keep this for backward compatibility with views
            public string ModuleTitle { get; set; } = string.Empty;
        }

        public class SubmissionData
        {
            public int SubmissionId { get; set; }
            public int UserId { get; set; }
            public string StudentName { get; set; } = string.Empty;
            public string SubmissionText { get; set; } = string.Empty;
            public string FileUrl { get; set; } = string.Empty;
            public DateTime SubmittedOn { get; set; }
            public int? Grade { get; set; }
            public string Feedback { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool IsLate { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            var userRole = HttpContext.Session.GetString("UserRole");

            if (userId == null || userRole != "INSTRUCTOR")
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment details
                var assignmentResult = await connection.QueryFirstOrDefaultAsync<Assignment>(
                    @"SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        c.TITLE AS CourseTitle,
                        c.COURSE_ID AS CourseId,
                        '' AS ModuleTitle  -- Empty module title for compatibility
                      FROM ASSIGNMENTS a
                      JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                      WHERE a.ASSIGNMENT_ID = @AssignmentId
                        AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId, InstructorId = userId });

                if (assignmentResult == null)
                {
                    ModelState.AddModelError("", "Assignment not found or you don't have permission to view it");
                    return Page();
                }

                AssignmentDetails = assignmentResult;
                AssignmentExists = true;

                // Get submissions for this assignment
                Submissions = (await connection.QueryAsync<SubmissionData>(
                    @"SELECT 
                        s.SUBMISSION_ID AS SubmissionId,
                        s.USER_ID AS UserId,
                        u.FULL_NAME AS StudentName,
                        s.SUBMISSION_TEXT AS SubmissionText,
                        s.FILE_URL AS FileUrl,
                        s.SUBMITTED_ON AS SubmittedOn,
                        s.GRADE AS Grade,
                        s.FEEDBACK AS Feedback,
                        s.STATUS AS Status,
                        CASE WHEN s.SUBMITTED_ON > a.DUE_DATE THEN 1 ELSE 0 END AS IsLate
                      FROM ASSIGNMENT_SUBMISSIONS s
                      JOIN USERS u ON s.USER_ID = u.USER_ID
                      JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                      WHERE s.ASSIGNMENT_ID = @AssignmentId
                      ORDER BY s.SUBMITTED_ON DESC",
                    new { AssignmentId })).ToList();

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostGradeAsync(int submissionId, int grade, string feedback, int assignmentId)
        {
            // Check if user is logged in
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            var userRole = HttpContext.Session.GetString("UserRole");

            // Redirect to login if not authenticated
            if (userId == null || userRole != "INSTRUCTOR")
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First, set the AssignmentId property so it's available for the page
                AssignmentId = assignmentId;

                // Verify submission belongs to an assignment from instructor's course
                var isValid = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT COUNT(1) FROM ASSIGNMENT_SUBMISSIONS s
              JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
              JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
              WHERE s.SUBMISSION_ID = @SubmissionId 
                AND c.CREATED_BY = @InstructorId",
                    new { SubmissionId = submissionId, InstructorId = userId });

                if (!isValid)
                {
                    TempData["ErrorMessage"] = "You don't have permission to grade this submission.";
                    return RedirectToPage(new { AssignmentId = assignmentId });
                }

                // Get max score for the assignment
                var maxScore = await connection.ExecuteScalarAsync<int>(
                    @"SELECT a.MAX_SCORE FROM ASSIGNMENT_SUBMISSIONS s
              JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
              WHERE s.SUBMISSION_ID = @SubmissionId",
                    new { SubmissionId = submissionId });

                if (grade < 0 || grade > maxScore)
                {
                    TempData["ErrorMessage"] = $"Grade must be between 0 and {maxScore}";
                    return RedirectToPage(new { AssignmentId = assignmentId });
                }

                // Update the submission with grade and feedback
                await connection.ExecuteAsync(
                    @"UPDATE ASSIGNMENT_SUBMISSIONS 
              SET GRADE = @Grade, 
                  FEEDBACK = @Feedback,
                  STATUS = 'graded'
              WHERE SUBMISSION_ID = @SubmissionId",
                    new { SubmissionId = submissionId, Grade = grade, Feedback = feedback });

                return RedirectToPage(new { AssignmentId = assignmentId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error grading submission: {ex.Message}";
                return RedirectToPage(new { AssignmentId = assignmentId });
            }
        }
    }
}