using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Courses
{
    public class IndexModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        public List<Course> Courses { get; set; } = new List<Course>();

        public async Task<IActionResult> OnGetAsync()
        {
            //var userId = HttpContext.Session.GetInt32("UserId");
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }

            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                Courses = (await connection.QueryAsync<Course>(@"
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.DESCRIPTION AS Description,
                        (SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE COURSE_ID = c.COURSE_ID) AS StudentCount,
                        ISNULL((SELECT AVG(RATING) FROM REVIEWS WHERE COURSE_ID = c.COURSE_ID), 0) AS Rating,
                        c.IS_ACTIVE AS IsActive,
                        c.CREATION_DATE AS CreatedDate
                    FROM COURSES c
                    WHERE c.CREATED_BY = @InstructorId
                    ORDER BY c.CREATION_DATE DESC",
                    new { InstructorId = userId })).ToList();

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDelete(int id)
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                var affectedRows = await connection.ExecuteAsync(@"
                    DELETE FROM COURSES 
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId = id, InstructorId = userId });

                return affectedRows > 0 ? RedirectToPage() : NotFound();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error deleting course");
                return Page();
            }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int StudentCount { get; set; }
            public decimal Rating { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedDate { get; set; }
        }
    }
}