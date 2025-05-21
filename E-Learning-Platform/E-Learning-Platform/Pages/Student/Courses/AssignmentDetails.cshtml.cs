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
