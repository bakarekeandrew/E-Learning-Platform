using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class ModulesModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        public List<Module> Modules { get; set; } = new List<Module>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public int? SelectedCourseId { get; set; }

        public async Task<IActionResult> OnGetAsync(int? courseId = null)
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

            SelectedCourseId = courseId;

            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                // Get instructor's courses
                Courses = (await connection.QueryAsync<Course>(@"
                    SELECT 
                        COURSE_ID AS CourseId,
                        TITLE AS Title
                    FROM COURSES
                    WHERE CREATED_BY = @InstructorId
                    ORDER BY TITLE",
                    new { InstructorId = userId })).ToList();

                // If no course is selected and instructor has courses, select the first one
                if (!SelectedCourseId.HasValue && Courses.Count > 0)
                {
                    SelectedCourseId = Courses[0].CourseId;
                }

                if (SelectedCourseId.HasValue)
                {
                    Modules = (await connection.QueryAsync<Module>(@"
    SELECT 
        MODULE_ID AS ModuleId,
        TITLE AS Title,
        DESCRIPTION AS Description,
        SEQUENCE_NUMBER AS SequenceNumber,
        IS_FREE AS IsFree
    FROM MODULES
    WHERE COURSE_ID = @CourseId
    ORDER BY SEQUENCE_NUMBER",
     new { CourseId = SelectedCourseId })).ToList();
                }

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
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

                // Verify that the module belongs to a course owned by this instructor
                var courseId = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT m.COURSE_ID
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId = id, InstructorId = userId });

                if (!courseId.HasValue)
                {
                    return NotFound();
                }

                // Delete the module
                await connection.ExecuteAsync(@"
                    DELETE FROM MODULES
                    WHERE MODULE_ID = @ModuleId",
                    new { ModuleId = id });

                return RedirectToPage(new { courseId = courseId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error deleting module: " + ex.Message);
                return RedirectToPage();
            }
        }

        public class Module
        {
            public int ModuleId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int SequenceNumber { get; set; }
            public bool IsFree { get; set; }
            public DateTime? CreatedDate { get; set; }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
        }
    }
}