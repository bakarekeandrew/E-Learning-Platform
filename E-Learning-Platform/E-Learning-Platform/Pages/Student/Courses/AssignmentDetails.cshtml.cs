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