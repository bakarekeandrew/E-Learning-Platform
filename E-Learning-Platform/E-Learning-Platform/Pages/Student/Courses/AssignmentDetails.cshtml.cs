using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class AssignmentDetailsModel : StudentPageModel
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AssignmentDetailsModel(
            ILogger<AssignmentDetailsModel> logger,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor)
            : base(logger, configuration)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }  // Assignment ID

        [BindProperty(SupportsGet = true)]
        public int? SubmissionId { get; set; }

        [BindProperty]
        public AssignmentSubmission Submission { get; set; }

        public AssignmentDetails Assignment { get; set; }
        public bool AssignmentExists { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                var studentId = GetStudentId();
                _logger.LogInformation("Loading assignment details for Assignment ID: {AssignmentId}, Student ID: {StudentId}", 
                    Id, studentId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First verify the assignment exists and student is enrolled in the course
                var assignmentCheck = await connection.QueryFirstOrDefaultAsync<AssignmentCheck>(@"
                    SELECT 
                        a.ASSIGNMENT_ID,
                        a.COURSE_ID,
                        c.TITLE AS CourseTitle,
                        ce.ENROLLMENT_ID
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @StudentId
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = Id, StudentId = studentId });

                if (assignmentCheck == null)
                {
                    _logger.LogWarning("Assignment not found. Assignment ID: {AssignmentId}", Id);
                    ModelState.AddModelError("", "Assignment not found.");
                    return Page();
                }

                if (assignmentCheck.ENROLLMENT_ID == null)
                {
                    _logger.LogWarning(
                        "Student not enrolled. Assignment ID: {AssignmentId}, Course ID: {CourseId}, Student ID: {StudentId}", 
                        Id, assignmentCheck.COURSE_ID, studentId);
                    ModelState.AddModelError("", "You are not enrolled in this course.");
                    return Page();
                }

                _logger.LogInformation("Assignment found. Course ID: {CourseId}, Course Title: {CourseTitle}", 
                    assignmentCheck.COURSE_ID, assignmentCheck.CourseTitle);

                // Get assignment details with submission if exists
                Assignment = await connection.QueryFirstOrDefaultAsync<AssignmentDetails>(@"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS CourseTitle,
                        s.SUBMISSION_ID AS SubmissionId,
                        s.SUBMISSION_TEXT AS SubmissionText,
                        s.FILE_URL AS FileUrl,
                        s.SUBMITTED_ON AS SubmittedOn,
                        s.GRADE AS Grade,
                        s.FEEDBACK AS Feedback,
                        s.STATUS AS Status
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID 
                        AND s.USER_ID = @StudentId
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = Id, StudentId = studentId });

                if (Assignment == null)
                {
                    _logger.LogError("Failed to load assignment details after verification passed. Assignment ID: {AssignmentId}", 
                        Id);
                    ModelState.AddModelError("", "Error loading assignment details.");
                    return Page();
                }

                AssignmentExists = true;
                _logger.LogInformation("Successfully loaded assignment details. Title: {Title}", Assignment.Title);

                // Initialize new submission if none exists
                if (Assignment.SubmissionId == 0)
                {
                    Submission = new AssignmentSubmission();
                }

                return Page();
            }, "Error loading assignment details");
        }

        public async Task<IActionResult> OnPostAsync(IFormFile file)
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                var studentId = GetStudentId();
                _logger.LogInformation("Processing submission for Assignment ID: {AssignmentId}, Student ID: {StudentId}", 
                    Id, studentId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify student is enrolled and assignment exists
                var assignmentCheck = await connection.QueryFirstOrDefaultAsync<AssignmentCheck>(@"
                    SELECT 
                        a.ASSIGNMENT_ID,
                        a.COURSE_ID,
                        ce.ENROLLMENT_ID,
                        a.DUE_DATE
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @StudentId
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = Id, StudentId = studentId });

                if (assignmentCheck == null)
                {
                    ModelState.AddModelError("", "Assignment not found.");
                    return Page();
                }

                if (assignmentCheck.ENROLLMENT_ID == null)
                {
                    ModelState.AddModelError("", "You are not enrolled in this course.");
                    return Page();
                }

                // Handle file upload if provided
                string fileUrl = null;
                if (file != null && file.Length > 0)
                {
                    try
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "assignments");
                        Directory.CreateDirectory(uploadsFolder);

                        var uniqueFileName = $"{studentId}_{Id}_{DateTime.Now:yyyyMMddHHmmss}_{file.FileName}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(fileStream);
                        }

                        fileUrl = $"/uploads/assignments/{uniqueFileName}";
                        _logger.LogInformation("File uploaded successfully: {FileUrl}", fileUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error uploading file for Assignment ID: {AssignmentId}", Id);
                        ModelState.AddModelError("", "Error uploading file. Please try again.");
                        return Page();
                    }
                }

                // Update or insert submission
                if (Submission.SubmissionId > 0)
                {
                    await connection.ExecuteAsync(@"
                        UPDATE ASSIGNMENT_SUBMISSIONS 
                        SET SUBMISSION_TEXT = @SubmissionText,
                            FILE_URL = COALESCE(@FileUrl, FILE_URL),
                            SUBMITTED_ON = GETDATE(),
                            STATUS = 'Submitted'
                        WHERE SUBMISSION_ID = @SubmissionId 
                        AND USER_ID = @StudentId",
                        new { 
                            SubmissionId = Submission.SubmissionId,
                            SubmissionText = Submission.SubmissionText,
                            FileUrl = fileUrl,
                            StudentId = studentId
                        });

                    _logger.LogInformation("Updated submission {SubmissionId} for Assignment {AssignmentId}", 
                        Submission.SubmissionId, Id);
                }
                else
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO ASSIGNMENT_SUBMISSIONS (
                            USER_ID,
                            ASSIGNMENT_ID,
                            SUBMISSION_TEXT,
                            FILE_URL,
                            SUBMITTED_ON,
                            STATUS
                        ) VALUES (
                            @StudentId,
                            @AssignmentId,
                            @SubmissionText,
                            @FileUrl,
                            GETDATE(),
                            'Submitted'
                        )",
                        new { 
                            StudentId = studentId,
                            AssignmentId = Id,
                            SubmissionText = Submission.SubmissionText,
                            FileUrl = fileUrl
                        });

                    _logger.LogInformation("Created new submission for Assignment {AssignmentId}", Id);
                }

                TempData["SuccessMessage"] = "Assignment submitted successfully!";
                return RedirectToPage("/Student/Courses/Assignments", new { courseId = assignmentCheck.COURSE_ID });
            }, "Error submitting assignment");
        }

        private class AssignmentCheck
        {
            public int ASSIGNMENT_ID { get; set; }
            public int COURSE_ID { get; set; }
            public string CourseTitle { get; set; }
            public int? ENROLLMENT_ID { get; set; }
            public DateTime? DUE_DATE { get; set; }
        }

        public class AssignmentDetails
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; }
            public string Instructions { get; set; }
            public DateTime DueDate { get; set; }
            public int MaxScore { get; set; }
            public int CourseId { get; set; }
            public string CourseTitle { get; set; }
            public int SubmissionId { get; set; }
            public string SubmissionText { get; set; }
            public string FileUrl { get; set; }
            public DateTime? SubmittedOn { get; set; }
            public decimal? Grade { get; set; }
            public string Feedback { get; set; }
            public string Status { get; set; }
        }

        public class AssignmentSubmission
        {
            public int SubmissionId { get; set; }
            public string SubmissionText { get; set; }
            public string FileUrl { get; set; }
        }
    }
}