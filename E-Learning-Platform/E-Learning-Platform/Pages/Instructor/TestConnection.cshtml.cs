using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Text;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Instructor
{
    public class TestConnectionModel : InstructorPageModel
    {
        public string DatabaseStatus { get; set; }
        public string SessionStatus { get; set; }
        public string AuthStatus { get; set; }
        public string UserDetails { get; set; }
        public string ClaimsDetails { get; set; }
        public int? CurrentUserId { get; set; }

        public TestConnectionModel(ILogger<TestConnectionModel> logger, IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Test Session
            CurrentUserId = GetInstructorId();
            
            var sb = new StringBuilder();
            sb.AppendLine("Session Data:");
            sb.AppendLine($"- UserId from Session: {HttpContext.Session.GetInt32("UserId")}");
            sb.AppendLine($"- UserRole from Session: {HttpContext.Session.GetString("UserRole")}");
            sb.AppendLine($"- UserName from Session: {HttpContext.Session.GetString("UserName")}");
            SessionStatus = sb.ToString();

            // Test Claims
            sb.Clear();
            sb.AppendLine("Claims Data:");
            foreach (var claim in User.Claims)
            {
                sb.AppendLine($"- {claim.Type}: {claim.Value}");
            }
            ClaimsDetails = sb.ToString();

            // Test Authentication
            AuthStatus = User.Identity?.IsAuthenticated == true
                ? $"Authenticated as {User.Identity.Name}. Roles: {string.Join(", ", User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value))}"
                : "Not authenticated";

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                
                sb.Clear();
                
                // Get user details from USERS table
                if (User.Identity?.Name != null)
                {
                    var userDetails = await connection.QueryFirstOrDefaultAsync(@"
                        SELECT 
                            U.USER_ID,
                            U.EMAIL,
                            U.FULL_NAME,
                            R.ROLE_NAME,
                            (SELECT COUNT(*) FROM COURSES WHERE CREATED_BY = U.USER_ID) as CourseCount
                        FROM USERS U
                        JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID
                        WHERE U.EMAIL = @Email",
                        new { Email = User.FindFirst(ClaimTypes.Email)?.Value });

                    if (userDetails != null)
                    {
                        sb.AppendLine("Database User Details:");
                        sb.AppendLine($"- Database User ID: {userDetails.USER_ID}");
                        sb.AppendLine($"- Email: {userDetails.EMAIL}");
                        sb.AppendLine($"- Name: {userDetails.FULL_NAME}");
                        sb.AppendLine($"- Role: {userDetails.ROLE_NAME}");
                        sb.AppendLine($"- Course Count: {userDetails.CourseCount}");

                        // Get actual courses for this instructor
                        var courses = await connection.QueryAsync(@"
                            SELECT COURSE_ID, TITLE, CREATED_BY 
                            FROM COURSES 
                            WHERE CREATED_BY = @UserId",
                            new { UserId = userDetails.USER_ID });

                        sb.AppendLine("\nCourses in Database:");
                        foreach (var course in courses)
                        {
                            sb.AppendLine($"- Course ID: {course.COURSE_ID}, Title: {course.TITLE}, Created By: {course.CREATED_BY}");
                        }
                    }
                }

                UserDetails = sb.ToString();
                
                DatabaseStatus = "Database connection successful";
            }
            catch (Exception ex)
            {
                DatabaseStatus = $"Connection failed: {ex.Message}";
                _logger.LogError(ex, "Database connection test failed");
            }

            return Page();
        }
    }
} 