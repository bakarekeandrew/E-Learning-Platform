using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class CreateAssignmentModel : PageModel
    {
        private readonly string _connectionString;

        public CreateAssignmentModel()
        {
            _connectionString = "Data Source=ABAKAREKE25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        [BindProperty]
        public int CourseId { get; set; }

        [BindProperty]
        public string Title { get; set; }

        [BindProperty]
        public string Instructions { get; set; }

        [BindProperty]
        public DateTime? DueDate { get; set; }

        [BindProperty]
        public int MaxScore { get; set; } = 100;

        public async Task<IActionResult> OnPostAsync()
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

            if (!ModelState.IsValid)
            {
                return RedirectToPage("/Instructor/Content/Assignments");
            }

            try
            {
                // Verify that the course is owned by this instructor
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var courseInfo = await connection.QueryFirstOrDefaultAsync<CourseInfo>(
                    @"SELECT CREATED_BY AS InstructorId 
                      FROM COURSES
                      WHERE COURSE_ID = @CourseId",
                    new { CourseId });

                if (courseInfo == null || courseInfo.InstructorId != userId)
                {
                    return Forbid();
                }

                // Insert the new assignment
                await connection.ExecuteAsync(
                    @"INSERT INTO ASSIGNMENTS (COURSE_ID, TITLE, INSTRUCTIONS, DUE_DATE, MAX_SCORE)
                      VALUES (@CourseId, @Title, @Instructions, @DueDate, @MaxScore)",
                    new { CourseId, Title, Instructions, DueDate, MaxScore });

                return RedirectToPage("/Instructor/Content/Assignments",
                    new { SelectedCourseId = CourseId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error creating assignment: {ex.Message}";
                return RedirectToPage("/Instructor/Content/Assignments");
            }
        }

        private class CourseInfo
        {
            public int InstructorId { get; set; }
        }
    }
}