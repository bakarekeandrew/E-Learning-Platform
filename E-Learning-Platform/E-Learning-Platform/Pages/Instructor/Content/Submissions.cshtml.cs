using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class SubmissionsModel : InstructorPageModel
    {
        [BindProperty(SupportsGet = true)]
        public int AssignmentId { get; set; }

        public Assignment AssignmentDetails { get; set; } = new Assignment();
        public List<SubmissionData> Submissions { get; set; } = new List<SubmissionData>();
        public bool AssignmentExists { get; set; } = false;

        public SubmissionsModel(ILogger<SubmissionsModel> logger, IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        public class Assignment
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Instructions { get; set; } = string.Empty;
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; } = 100;
            public string CourseTitle { get; set; } = string.Empty;
            public int CourseId { get; set; }
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

        public async Task<IActionResult> OnGetAsync(int assignmentId)
        {
            AssignmentId = assignmentId;
            
            try
            {
                var instructorId = GetInstructorId();
                if (instructorId == null)
                {
                    _logger.LogWarning("No instructor ID found in session");
                    TempData["ErrorMessage"] = "Please log in again.";
                    return RedirectToPage("/Login");
                }

                _logger.LogInformation("Loading submissions for Assignment ID: {AssignmentId}, Instructor ID: {InstructorId}", 
                    assignmentId, instructorId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First verify the assignment exists and belongs to the instructor
                var assignmentCheck = await connection.QueryFirstOrDefaultAsync<AssignmentCheck>(@"
                    SELECT 
                        a.ASSIGNMENT_ID,
                        c.CREATED_BY AS InstructorId,
                        c.COURSE_ID,
                        m.MODULE_ID
                    FROM ASSIGNMENTS a
                    JOIN MODULES m ON a.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignmentCheck == null)
                {
                    _logger.LogWarning("Assignment not found. Assignment ID: {AssignmentId}", assignmentId);
                    TempData["ErrorMessage"] = "Assignment not found.";
                    return RedirectToPage("/Instructor/Content/Assignments");
                }

                _logger.LogInformation("Assignment found. Course ID: {CourseId}, Module ID: {ModuleId}, Owner ID: {OwnerId}", 
                    assignmentCheck.COURSE_ID, assignmentCheck.MODULE_ID, assignmentCheck.InstructorId);

                if (assignmentCheck.InstructorId != instructorId)
                {
                    _logger.LogWarning(
                        "Permission denied. Assignment ID: {AssignmentId}, Owner ID: {OwnerId}, Requester ID: {RequesterId}", 
                        assignmentId, assignmentCheck.InstructorId, instructorId);
                    TempData["ErrorMessage"] = "You don't have permission to view this assignment.";
                    return RedirectToPage("/Instructor/Content/Assignments");
                }

                // Get assignment details
                AssignmentDetails = await connection.QueryFirstOrDefaultAsync<Assignment>(@"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        c.TITLE AS CourseTitle,
                        c.COURSE_ID AS CourseId,
                        m.TITLE AS ModuleTitle
                    FROM ASSIGNMENTS a
                    JOIN MODULES m ON a.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (AssignmentDetails == null)
                {
                    _logger.LogError("Failed to load assignment details after verification passed. Assignment ID: {AssignmentId}", 
                        assignmentId);
                    TempData["ErrorMessage"] = "Error loading assignment details.";
                    return RedirectToPage("/Instructor/Content/Assignments");
                }

                AssignmentExists = true;
                _logger.LogInformation("Successfully loaded assignment details. Title: {Title}", AssignmentDetails.Title);

                // Get submissions
                Submissions = (await connection.QueryAsync<SubmissionData>(@"
                    SELECT 
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
                    new { AssignmentId = assignmentId })).AsList();

                _logger.LogInformation("Successfully loaded {Count} submissions for assignment", Submissions.Count);

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request for assignment {AssignmentId}", assignmentId);
                TempData["ErrorMessage"] = "An error occurred while loading the assignment.";
                return RedirectToPage("/Instructor/Content/Assignments");
            }
        }

        public async Task<IActionResult> OnPostGradeAsync(int submissionId, int grade, string feedback, int assignmentId)
        {
            return await ExecuteDbOperationAsync(async () =>
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
                    new { SubmissionId = submissionId, InstructorId = GetInstructorId() });

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
            }, "Error grading submission");
        }

        private class AssignmentCheck
        {
            public int ASSIGNMENT_ID { get; set; }
            public int InstructorId { get; set; }
            public int COURSE_ID { get; set; }
            public int MODULE_ID { get; set; }
        }
    }
}