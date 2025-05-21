using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Student.Courses
{
    [Authorize(Policy = "StudentOnly")]
    public class AssignmentDetailsModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<AssignmentDetailsModel> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AssignmentDetailsModel(
            ILogger<AssignmentDetailsModel> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;Initial Catalog=ONLINE_LEARNING_PLATFORM;Integrated Security=True;TrustServerCertificate=True";
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }  // Assignment ID

        [BindProperty(SupportsGet = true)]
        public int? SubmissionId { get; set; }

        [BindProperty]
        public AssignmentSubmission Submission { get; set; }

        public AssignmentDetails Assignment { get; set; }
        public string ErrorMessage { get; set; }
        public string SuccessMessage { get; set; }
        public int CurrentUserId { get; set; }

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
                _logger.LogInformation($"Loading assignment details for user ID: {CurrentUserId}, assignment ID: {Id}");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if assignment exists first
                var assignmentExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT CASE WHEN EXISTS(SELECT 1 FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId) THEN 1 ELSE 0 END",
                    new { AssignmentId = Id });

                if (!assignmentExists)
                {
                    ErrorMessage = "Assignment not found in database.";
                    _logger.LogWarning($"Assignment ID {Id} not found in database.");
                    return Page();
                }

                // Get assignment details
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
                        AND s.USER_ID = @UserId
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { UserId = CurrentUserId, AssignmentId = Id });

                if (Assignment == null)
                {
                    ErrorMessage = "Assignment not found or cannot be loaded.";
                    _logger.LogError($"Assignment ID {Id} query returned NULL even though it exists in the database.");
                    return Page();
                }

                // Check if the user is enrolled in this course
                var isEnrolled = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT CASE WHEN EXISTS(
                        SELECT 1 FROM COURSE_ENROLLMENTS
                        WHERE USER_ID = @UserId AND COURSE_ID = @CourseId
                    ) THEN 1 ELSE 0 END",
                    new { UserId = CurrentUserId, CourseId = Assignment.CourseId });

                if (!isEnrolled)
                {
                    ErrorMessage = "You are not enrolled in this course.";
                    return Page();
                }

                // Make sure we handle null values properly to avoid errors
                if (Assignment.SubmissionId == 0)
                {
                    // Explicitly set properties that would be NULL from database to avoid NullReferenceException
                    Assignment.SubmissionText = null;
                    Assignment.FileUrl = null;
                    Assignment.SubmittedOn = null;
                    Assignment.Grade = null;
                    Assignment.Feedback = null;
                    Assignment.Status = "Not Submitted";
                }

                // Initialize submission if not exists
                if (Assignment.SubmissionId == 0 && Submission == null)
                {
                    Submission = new AssignmentSubmission();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading assignment details for assignment ID {AssignmentId} and user {UserId}", Id, CurrentUserId);
                ErrorMessage = "An error occurred while loading assignment details: " + ex.Message;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync(IFormFile file)
        {
            try
            {
                // Get current user ID from claims or session
                CurrentUserId = GetCurrentUserId();
                _logger.LogInformation($"Submitting assignment for user ID: {CurrentUserId}, assignment ID: {Id}");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First make sure the assignment exists
                var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ASSIGNMENT_ID, COURSE_ID FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = Id });

                if (assignment == null)
                {
                    ErrorMessage = "Assignment not found.";
                    return Page();
                }

                // Check if the user is enrolled in this course
                var isEnrolled = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT CASE WHEN EXISTS(
                        SELECT 1 FROM COURSE_ENROLLMENTS
                        WHERE USER_ID = @UserId AND COURSE_ID = @CourseId
                    ) THEN 1 ELSE 0 END",
                    new { UserId = CurrentUserId, CourseId = assignment.COURSE_ID });

                if (!isEnrolled)
                {
                    ErrorMessage = "You are not enrolled in this course.";
                    return Page();
                }

                // Handle file upload
                string fileUrl = null;
                if (file != null && file.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }
                    fileUrl = "/uploads/" + uniqueFileName;
                }

                // Check if submission exists
                var existingSubmission = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT SUBMISSION_ID FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = @AssignmentId AND USER_ID = @UserId",
                    new { AssignmentId = Id, UserId = CurrentUserId });

                if (existingSubmission != null)
                {
                    // Update existing submission
                    await connection.ExecuteAsync(@"
                        UPDATE ASSIGNMENT_SUBMISSIONS SET
                            SUBMISSION_TEXT = @SubmissionText,
                            FILE_URL = CASE WHEN @FileUrl IS NULL THEN FILE_URL ELSE @FileUrl END,
                            SUBMITTED_ON = GETDATE(),
                            STATUS = 'Submitted'
                        WHERE SUBMISSION_ID = @SubmissionId",
                        new
                        {
                            SubmissionText = Submission.SubmissionText,
                            FileUrl = fileUrl,
                            SubmissionId = existingSubmission.SUBMISSION_ID
                        });
                }
                else
                {
                    // Create new submission
                    await connection.ExecuteAsync(@"
                        INSERT INTO ASSIGNMENT_SUBMISSIONS (
                            ASSIGNMENT_ID,
                            USER_ID,
                            SUBMISSION_TEXT,
                            FILE_URL,
                            SUBMITTED_ON,
                            STATUS
                        ) VALUES (
                            @AssignmentId,
                            @UserId,
                            @SubmissionText,
                            @FileUrl,
                            GETDATE(),
                            'Submitted'
                        )",
                        new
                        {
                            AssignmentId = Id,
                            UserId = CurrentUserId,
                            SubmissionText = Submission.SubmissionText,
                            FileUrl = fileUrl
                        });
                }

                SuccessMessage = "Assignment submitted successfully!";
                return RedirectToPage("/Student/Courses/Assignments", new { courseId = assignment.COURSE_ID });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting assignment {AssignmentId} for user {UserId}", Id, CurrentUserId);
                ErrorMessage = "An error occurred while submitting the assignment: " + ex.Message;
                return await OnGetAsync(); // Reload the page with error
            }
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
            public string SubmissionText { get; set; }
            public string FileUrl { get; set; }
        }
    }
}