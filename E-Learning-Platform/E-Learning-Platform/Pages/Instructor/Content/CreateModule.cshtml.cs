using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class CreateModuleModel : InstructorPageModel
    {
        [BindProperty(SupportsGet = true)]
        public int CourseId { get; set; }

        public required string CourseName { get; set; }

        [BindProperty]
        public ModuleInput Module { get; set; } = new ModuleInput
        {
            Title = string.Empty,
            Description = string.Empty
        };

        public CreateModuleModel(ILogger<CreateModuleModel> logger, IConfiguration configuration)
            : base(logger, configuration)
        {
        }

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
            public bool IsFree { get; set; } = false;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify this instructor owns the course
                var course = await connection.QueryFirstOrDefaultAsync<CourseInfo>(@"
                    SELECT 
                        COURSE_ID AS CourseId,
                        TITLE AS Title
                    FROM COURSES
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId, InstructorId = GetInstructorId() });

                if (course == null)
                {
                    return RedirectToPage("Modules");
                }

                CourseName = course.Title;

                // Set default sequence number to be after the last module
                var highestOrder = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT MAX(SEQUENCE_NUMBER) 
                    FROM MODULES 
                    WHERE COURSE_ID = @CourseId",
                    new { CourseId });

                Module.SequenceNumber = (highestOrder ?? 0) + 1;

                return Page();
            }, "Error loading course information");
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify this instructor owns the course
                var ownsCourse = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) FROM COURSES 
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId, InstructorId = GetInstructorId() });

                if (!ownsCourse)
                {
                    return RedirectToPage("Modules");
                }

                // Insert new module
                await connection.ExecuteAsync(@"
                    INSERT INTO MODULES (
                        COURSE_ID,
                        TITLE,
                        DESCRIPTION,
                        SEQUENCE_NUMBER,
                        IS_FREE,
                        DURATION_MINUTES
                    ) VALUES (
                        @CourseId,
                        @Title,
                        @Description,
                        @SequenceNumber,
                        @IsFree,
                        0
                    )",
                    new
                    {
                        CourseId,
                        Module.Title,
                        Module.Description,
                        Module.SequenceNumber,
                        Module.IsFree
                    });

                TempData["SuccessMessage"] = "Module created successfully.";
                return RedirectToPage("Modules", new { courseId = CourseId });
            }, "Error creating module");
        }

        private class CourseInfo
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
        }
    }
}