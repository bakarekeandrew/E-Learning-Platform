using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class CreateQuizModel : InstructorPageModel
    {
        [BindProperty(SupportsGet = true)]
        public int ModuleId { get; set; }

        public required string ModuleTitle { get; set; }
        public int CourseId { get; set; }

        [BindProperty]
        public QuizInput Quiz { get; set; } = new QuizInput
        {
            Title = string.Empty,
            Description = string.Empty
        };

        public CreateQuizModel(ILogger<CreateQuizModel> logger, IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        public class QuizInput
        {
            [Required]
            [StringLength(100)]
            public required string Title { get; set; }

            [StringLength(500)]
            public required string Description { get; set; }

            [Required]
            [Range(1, 100)]
            [Display(Name = "Passing Score")]
            public int PassingScore { get; set; } = 70;

            [Display(Name = "Time Limit (minutes)")]
            [Range(0, 300)]
            public int TimeLimitMinutes { get; set; } = 30;

            [Required]
            [Display(Name = "Maximum Attempts")]
            [Range(1, 10)]
            public int MaxAttempts { get; set; } = 3;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify this instructor owns the module
                var moduleInfo = await connection.QueryFirstOrDefaultAsync<ModuleInfo>(@"
                    SELECT 
                        m.MODULE_ID AS ModuleId,
                        m.TITLE AS Title,
                        m.COURSE_ID AS CourseId
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId, InstructorId = GetInstructorId() });

                if (moduleInfo == null)
                {
                    return RedirectToPage("Quizzes");
                }

                ModuleTitle = moduleInfo.Title;
                CourseId = moduleInfo.CourseId;

                return Page();
            }, "Error loading module information");
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

                // Verify this instructor owns the module
                var ownsModule = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId, InstructorId = GetInstructorId() });

                if (!ownsModule)
                {
                    return RedirectToPage("Quizzes");
                }

                // Insert new quiz and get the new quiz ID
                var quizId = await connection.ExecuteScalarAsync<int>(@"
                    INSERT INTO QUIZZES (
                        MODULE_ID,
                        TITLE,
                        DESCRIPTION,
                        PASSING_SCORE,
                        TIME_LIMIT_MINUTES,
                        MAX_ATTEMPTS
                    ) VALUES (
                        @ModuleId,
                        @Title,
                        @Description,
                        @PassingScore,
                        @TimeLimitMinutes,
                        @MaxAttempts
                    );
                    SELECT SCOPE_IDENTITY();",
                    new
                    {
                        ModuleId,
                        Quiz.Title,
                        Quiz.Description,
                        Quiz.PassingScore,
                        Quiz.TimeLimitMinutes,
                        Quiz.MaxAttempts
                    });

                TempData["SuccessMessage"] = "Quiz created successfully.";
                return RedirectToPage("QuizQuestions", new { quizId });
            }, "Error creating quiz");
        }

        private class ModuleInfo
        {
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public int CourseId { get; set; }
        }
    }
}