using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class CreateQuizModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int ModuleId { get; set; }

        public required string ModuleTitle { get; set; }
        public int CourseId { get; set; }

        [BindProperty]
        public QuizInput Quiz { get; set; } = new QuizInput
        {
            Title = string.Empty, // Initialize required property
            Description = string.Empty // Initialize required property
        };


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

                // Verify this instructor owns the module
                var moduleInfo = await connection.QueryFirstOrDefaultAsync<ModuleInfo>(@"
                    SELECT 
                        m.MODULE_ID AS ModuleId,
                        m.TITLE AS Title,
                        m.COURSE_ID AS CourseId
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId, InstructorId = userId });

                if (moduleInfo == null)
                {
                    return NotFound();
                }

                ModuleTitle = moduleInfo.Title ?? string.Empty;
                CourseId = moduleInfo.CourseId;

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return RedirectToPage("Quizzes", new { moduleId = ModuleId });
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

                // Verify this instructor owns the module
                var ownsModule = await connection.ExecuteScalarAsync<bool>(@"
            SELECT COUNT(1) 
            FROM MODULES m
            JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
            WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId, InstructorId = userId });

                if (!ownsModule)
                {
                    return NotFound();
                }

                // Insert new quiz
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
            SELECT SCOPE_IDENTITY();", // This gets the newly created quiz ID
                        new
                        {
                            ModuleId,
                            Quiz.Title,
                            Quiz.Description,
                            Quiz.PassingScore,
                            Quiz.TimeLimitMinutes,
                            Quiz.MaxAttempts
                        });

                // Redirect to QuizQuestions page for the new quiz
                return RedirectToPage("QuizQuestions", new { quizId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error creating quiz: " + ex.Message);
                return Page();
            }
        }

        private class ModuleInfo
        {
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public int CourseId { get; set; }
        }
    }
}