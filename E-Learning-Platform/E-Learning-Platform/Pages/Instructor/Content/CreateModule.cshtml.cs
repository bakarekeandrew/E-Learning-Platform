using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class CreateModuleModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int CourseId { get; set; }

        public required string CourseName { get; set; }

        [BindProperty]
        public ModuleInput Module { get; set; } = new ModuleInput
        {
            Title = string.Empty, // Initialize required property
            Description = string.Empty // Initialize required property
        };


        public class ModuleInput
        {
            [Required]
            [StringLength(100)]
            public required string Title { get; set; }

            [Required]
            [StringLength(10000)]
            public required string Description { get; set; }

            [Required]
            [Range(1, 100)]
            [Display(Name = "Order")]
            public int SequenceNumber { get; set; } = 1;

            [Display(Name = "Free Module")]
            public bool IsFree { get; set; } = false; // Changed from IsActive to match database
        }

        public async Task<IActionResult> OnGetAsync()
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

                // Verify this instructor owns the course
                var course = await connection.QueryFirstOrDefaultAsync<CourseInfo>(@"
                    SELECT 
                        COURSE_ID AS CourseId,
                        TITLE AS Title
                    FROM COURSES
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId, InstructorId = userId });

                if (course == null)
                {
                    return NotFound();
                }

                CourseName = course.Title;

                // Set default sequence number to be after the last module
                var highestOrder = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT MAX(SEQUENCE_NUMBER) 
                    FROM MODULES 
                    WHERE COURSE_ID = @CourseId",
                    new { CourseId });

                if (highestOrder.HasValue)
                {
                    Module.SequenceNumber = highestOrder.Value + 1;
                }

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return RedirectToPage("Modules");
            }
        }

        public async Task<IActionResult> OnPostAsync()
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

            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                // Verify this instructor owns the course
                var ownsCourse = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) FROM COURSES 
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId, InstructorId = userId });

                if (!ownsCourse)
                {
                    return NotFound();
                }

                // Insert new module with correct column names
                // Insert new module with correct column names
                await connection.ExecuteAsync(@"
                        INSERT INTO MODULES (
                                  COURSE_ID,
                                  TITLE,
                                  DESCRIPTION,
                                  SEQUENCE_NUMBER,
                                  IS_FREE
                                  ) VALUES (
                                  @CourseId,
                                  @Title,
                                  @Description,
                                  @SequenceNumber,
                                  @IsFree
                                  )",
                    new
                    {
                        CourseId,
                        Module.Title,
                        Module.Description,
                        Module.SequenceNumber,
                        Module.IsFree
                    });

                return RedirectToPage("Modules", new { courseId = CourseId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error creating module: " + ex.Message);
                return Page();
            }
        }

        private class CourseInfo
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
        }
    }
}