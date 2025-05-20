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