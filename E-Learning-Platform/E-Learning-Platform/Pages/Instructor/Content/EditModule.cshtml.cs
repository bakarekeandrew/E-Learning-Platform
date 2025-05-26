using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    [Authorize(Roles = "INSTRUCTOR")]
    public class EditModuleModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        [BindProperty]
        public ModuleInput Module { get; set; } = new ModuleInput
        {
            Title = string.Empty,
            Description = string.Empty
        };


        public required string CourseName { get; set; }
        public int CourseId { get; set; }

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
            public int SequenceNumber { get; set; }

            [Display(Name = "Free Module")]
            public bool IsFree { get; set; }

            [Display(Name = "Duration (minutes)")]
            public int? DurationMinutes { get; set; }
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

                // Get module details and verify ownership
                var moduleInfo = await connection.QueryFirstOrDefaultAsync<ModuleInfo>(@"
                    SELECT 
                        m.MODULE_ID,
                        m.TITLE,
                        m.DESCRIPTION,
                        m.SEQUENCE_NUMBER,
                        m.IS_FREE,
                        m.DURATION_MINUTES,
                        m.COURSE_ID,
                        c.TITLE AS CourseTitle
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId = Id, InstructorId = userId });

                if (moduleInfo == null)
                {
                    return NotFound();
                }

                // Populate the model
                Module.Title = moduleInfo.TITLE ?? string.Empty;
                Module.Description = moduleInfo.DESCRIPTION ?? string.Empty;
                Module.SequenceNumber = moduleInfo.SEQUENCE_NUMBER;
                Module.IsFree = moduleInfo.IS_FREE;
                Module.DurationMinutes = moduleInfo.DURATION_MINUTES;
                CourseId = moduleInfo.COURSE_ID;
                CourseName = moduleInfo.COURSE_TITLE ?? string.Empty;

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
            ;
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

                // Verify ownership before updating
                var ownsModule = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId = Id, InstructorId = userId });

                if (!ownsModule)
                {
                    return NotFound();
                }

                // Update the module
                await connection.ExecuteAsync(@"
                    UPDATE MODULES SET
                        TITLE = @Title,
                        DESCRIPTION = @Description,
                        SEQUENCE_NUMBER = @SequenceNumber,
                        IS_FREE = @IsFree,
                        DURATION_MINUTES = @DurationMinutes
                    WHERE MODULE_ID = @ModuleId",
                    new
                    {
                        Module.Title,
                        Module.Description,
                        Module.SequenceNumber,
                        Module.IsFree,
                        Module.DurationMinutes,
                        ModuleId = Id
                    });

                // Get course ID for redirection
                var courseId = await connection.ExecuteScalarAsync<int>(
                    "SELECT COURSE_ID FROM MODULES WHERE MODULE_ID = @ModuleId",
                    new { ModuleId = Id });

                return RedirectToPage("Modules", new { courseId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error updating module: " + ex.Message);
                return Page();
            }
        }

        private class ModuleInfo
        {
            public int MODULE_ID { get; set; }
            public required string TITLE { get; set; }
            public required string DESCRIPTION { get; set; }
            public int SEQUENCE_NUMBER { get; set; }
            public bool IS_FREE { get; set; }
            public int? DURATION_MINUTES { get; set; }
            public int COURSE_ID { get; set; }
            public required string COURSE_TITLE { get; set; }
        }
    }
}