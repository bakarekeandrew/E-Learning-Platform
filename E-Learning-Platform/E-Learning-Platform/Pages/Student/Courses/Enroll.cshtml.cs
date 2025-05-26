using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Dapper;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class EnrollModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<EnrollModel> _logger;
        private readonly INotificationService _notificationService;
        public string ErrorMessage { get; set; }
        public string SuccessMessage { get; set; }

        public EnrollModel(
            IConfiguration configuration, 
            ILogger<EnrollModel> logger,
            INotificationService notificationService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Check if already enrolled
                var isEnrolled = await connection.QueryFirstOrDefaultAsync<bool>(
                    "SELECT COUNT(1) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId = id });

                if (isEnrolled)
                {
                    _logger.LogWarning("User {UserId} attempted to enroll in course {CourseId} but is already enrolled", userId, id);
                    ErrorMessage = "You are already enrolled in this course.";
                    return Page();
                }

                // Get course details
                var courseDetails = await connection.QueryFirstOrDefaultAsync<CourseDetails>(
                    @"SELECT 
                        c.COURSE_ID,
                        c.TITLE,
                        c.DESCRIPTION,
                        u.FULL_NAME as InstructorName
                    FROM COURSES c
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    WHERE c.COURSE_ID = @CourseId AND c.IS_ACTIVE = 1",
                    new { CourseId = id });

                if (courseDetails == null)
                {
                    _logger.LogWarning("User {UserId} attempted to enroll in non-existent or inactive course {CourseId}", userId, id);
                    ErrorMessage = "This course is not available for enrollment.";
                    return Page();
                }

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // Enroll the user
                    var enrollmentResult = await connection.ExecuteAsync(
                        @"INSERT INTO COURSE_ENROLLMENTS (USER_ID, COURSE_ID, ENROLLMENT_DATE, STATUS)
                        VALUES (@UserId, @CourseId, GETUTCDATE(), 'ACTIVE')",
                        new { UserId = userId, CourseId = id },
                        transaction);

                    if (enrollmentResult > 0)
                    {
                        // Send notification about course enrollment
                        await _notificationService.CreateNotificationAsync(
                            int.Parse(userId),
                            "Course Enrollment",
                            $"You have been enrolled in the course: {courseDetails.Title}\nInstructor: {courseDetails.InstructorName}",
                            "success");

                        await transaction.CommitAsync();
                        _logger.LogInformation("Successfully enrolled user {UserId} in course {CourseId}", userId, id);
                        TempData["SuccessMessage"] = "Successfully enrolled in the course!";
                        return RedirectToPage("/Student/Courses");
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        _logger.LogWarning("Failed to enroll user {UserId} in course {CourseId}", userId, id);
                        ErrorMessage = "Failed to enroll in the course. Please try again.";
                        return Page();
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw;
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

        private class CourseDetails
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string InstructorName { get; set; }
        }
    }
} 