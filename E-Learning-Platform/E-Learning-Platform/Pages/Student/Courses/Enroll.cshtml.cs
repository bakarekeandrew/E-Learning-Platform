using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class EnrollModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<EnrollModel> _logger;

        public EnrollModel(ILogger<EnrollModel> logger)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public int id { get; set; }

        public string ErrorMessage { get; set; }
        public string SuccessMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                _logger.LogInformation("Starting enrollment process for course ID: {CourseId}", id);

                if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                {
                    _logger.LogWarning("User not logged in, redirecting to login page");
                    TempData["ErrorMessage"] = "Please log in to enroll in courses.";
                    return RedirectToPage("/Login");
                }

                var userId = BitConverter.ToInt32(userIdBytes, 0);
                _logger.LogInformation("User ID: {UserId} attempting to enroll in course ID: {CourseId}", userId, id);

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInformation("Database connection opened successfully");

                    // Check if user exists
                    var userExists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId });

                    if (!userExists)
                    {
                        ErrorMessage = "User not found. Please log in again.";
                        _logger.LogWarning("User not found. UserId: {UserId}", userId);
                        HttpContext.Session.Clear();
                        return RedirectToPage("/Login");
                    }

                    // Check if course exists and is active
                    var courseExists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM COURSES WHERE COURSE_ID = @CourseId AND IS_ACTIVE = 1",
                        new { CourseId = id });

                    _logger.LogInformation("Course exists check result: {CourseExists}", courseExists);

                    if (!courseExists)
                    {
                        ErrorMessage = "Course not found or is not available.";
                        _logger.LogWarning("Course not found or inactive. CourseId: {CourseId}", id);
                        return Page();
                    }

                    // Check if already enrolled
                    var isEnrolled = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                        new { UserId = userId, CourseId = id });

                    _logger.LogInformation("Already enrolled check result: {IsEnrolled}", isEnrolled);

                    if (isEnrolled)
                    {
                        ErrorMessage = "You are already enrolled in this course.";
                        _logger.LogWarning("User already enrolled. UserId: {UserId}, CourseId: {CourseId}", userId, id);
                        return Page();
                    }

                    // Enroll the user with transaction
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            var enrollmentResult = await connection.ExecuteAsync(
                                @"INSERT INTO COURSE_ENROLLMENTS (USER_ID, COURSE_ID, ENROLLMENT_DATE, STATUS) 
                          VALUES (@UserId, @CourseId, @EnrollmentDate, 'active')",
                                new
                                {
                                    UserId = userId,
                                    CourseId = id,
                                    EnrollmentDate = DateTime.UtcNow
                                },
                                transaction);

                            _logger.LogInformation("Enrollment result: {Result} rows affected", enrollmentResult);

                            if (enrollmentResult > 0)
                            {
                                transaction.Commit();
                                _logger.LogInformation("Successfully enrolled user {UserId} in course {CourseId}", userId, id);
                                TempData["SuccessMessage"] = "Successfully enrolled in the course!";
                                return RedirectToPage("/Student/Courses");
                            }
                            else
                            {
                                transaction.Rollback();
                                _logger.LogWarning("Failed to enroll user {UserId} in course {CourseId}", userId, id);
                                ErrorMessage = "Failed to enroll in the course. Please try again.";
                                return Page();
                            }
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            throw; // Re-throw to be caught by outer try-catch
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL Error enrolling in course. CourseId: {CourseId}, Error: {ErrorMessage}", id, ex.Message);
                ErrorMessage = $"Database error: {ex.Message}";
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enrolling in course. CourseId: {CourseId}", id);
                ErrorMessage = $"Error enrolling in course: {ex.Message}";
                return Page();
            }
        }
    }
} 