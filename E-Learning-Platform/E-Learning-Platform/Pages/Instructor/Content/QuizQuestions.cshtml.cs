using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class QuizQuestionsModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int QuizId { get; set; }

        public int ModuleId { get; set; }
        public string QuizTitle { get; set; }
        public int TotalPoints { get; set; }
        public List<QuestionWithOptions> Questions { get; set; } = new List<QuestionWithOptions>();

        public class QuestionWithOptions
        {
            public int QuestionId { get; set; }
            public string QuestionText { get; set; }
            public string QuestionType { get; set; }
            public int Points { get; set; }
            public List<QuestionOption> Options { get; set; } = new List<QuestionOption>();
        }

        public class QuestionOption
        {
            public int OptionId { get; set; }
            public string OptionText { get; set; }
            public bool IsCorrect { get; set; }
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

                // Verify this instructor owns the quiz
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        q.QUIZ_ID AS QuizId,
                        q.TITLE AS Title,
                        q.MODULE_ID AS ModuleId
                    FROM QUIZZES q
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE q.QUIZ_ID = @QuizId AND c.CREATED_BY = @InstructorId",
                    new { QuizId, InstructorId = userId });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                QuizTitle = quizInfo.Title;
                ModuleId = quizInfo.ModuleId;

                // Get all questions with their options
                var questions = await connection.QueryAsync<QuestionWithOptions>(@"
                    SELECT 
                        QUESTION_ID AS QuestionId,
                        QUESTION_TEXT AS QuestionText,
                        QUESTION_TYPE AS QuestionType,
                        POINTS AS Points
                    FROM QUIZ_QUESTIONS
                    WHERE QUIZ_ID = @QuizId
                    ORDER BY SEQUENCE_NUMBER",
                    new { QuizId });

                Questions = questions.ToList();

                foreach (var question in Questions)
                {
                    var options = await connection.QueryAsync<QuestionOption>(@"
                        SELECT 
                            OPTION_ID AS OptionId,
                            OPTION_TEXT AS OptionText,
                            IS_CORRECT AS IsCorrect
                        FROM QUIZ_OPTIONS
                        WHERE QUESTION_ID = @QuestionId
                        ORDER BY OPTION_ID",
                        new { question.QuestionId });

                    question.Options = options.ToList();
                }

                TotalPoints = Questions.Sum(q => q.Points);

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int questionId)
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

                // Verify this instructor owns the question
                var ownsQuestion = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM QUIZ_QUESTIONS qq
                    JOIN QUIZZES q ON qq.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE qq.QUESTION_ID = @QuestionId AND c.CREATED_BY = @InstructorId",
                    new { QuestionId = questionId, InstructorId = userId });

                if (!ownsQuestion)
                {
                    return NotFound();
                }

                // Delete options first
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZ_OPTIONS 
                    WHERE QUESTION_ID = @QuestionId",
                    new { QuestionId = questionId });

                // Then delete the question
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZ_QUESTIONS 
                    WHERE QUESTION_ID = @QuestionId",
                    new { QuestionId = questionId });

                return RedirectToPage(new { QuizId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error deleting question: " + ex.Message);
                return RedirectToPage(new { QuizId });
            }
        }

        private class QuizInfo
        {
            public int QuizId { get; set; }
            public string Title { get; set; }
            public int ModuleId { get; set; }
        }
    }
}