using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class EditQuestionModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int QuizId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int QuestionId { get; set; }

        public required string QuizTitle { get; set; }
        public int ModuleId { get; set; }

        [BindProperty]
        public required QuestionInput Question { get; set; }

        [BindProperty]
        public int CorrectOptionIndex { get; set; } = 0;

        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public class QuestionInput
        {
            public int QuestionId { get; set; }
            public required string QuestionText { get; set; }
            public required string QuestionType { get; set; }
            public int Points { get; set; }
            public int SequenceNumber { get; set; }
            public List<QuestionOption> Options { get; set; } = new List<QuestionOption>();
        }

        public class QuestionOption
        {
            public int OptionId { get; set; }
            public required string OptionText { get; set; }
            public bool IsCorrect { get; set; }
            public int SequenceNumber { get; set; }
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

                // Verify this instructor owns the question
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        q.QUIZ_ID AS QuizId,
                        q.TITLE AS Title,
                        q.MODULE_ID AS ModuleId
                    FROM QUIZ_QUESTIONS qq
                    JOIN QUIZZES q ON qq.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE qq.QUESTION_ID = @QuestionId AND c.CREATED_BY = @InstructorId",
                    new { QuestionId, InstructorId = userId });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                QuizTitle = quizInfo.Title;
                ModuleId = quizInfo.ModuleId;

                // Get question details
                var question = await connection.QueryFirstOrDefaultAsync<QuestionInput>(@"
                    SELECT 
                        QUESTION_TEXT AS QuestionText,
                        QUESTION_TYPE AS QuestionType,
                        POINTS AS Points,
                        SEQUENCE_NUMBER AS SequenceNumber
                    FROM QUIZ_QUESTIONS
                    WHERE QUESTION_ID = @QuestionId",
                    new { QuestionId });

                if (question == null)
                {
                    return NotFound();
                }

                Question = question;

                // Get options
                var options = await connection.QueryAsync<QuestionOption>(@"
                    SELECT 
                        OPTION_ID AS OptionId,
                        OPTION_TEXT AS OptionText,
                        IS_CORRECT AS IsCorrect,
                        SEQUENCE_NUMBER AS SequenceNumber
                    FROM QUIZ_OPTIONS
                    WHERE QUESTION_ID = @QuestionId
                    ORDER BY OPTION_ID",
                    new { QuestionId });

                Question.Options = options.ToList();

                // Find correct option index
                var correctOption = Question.Options.FirstOrDefault(o => o.IsCorrect);
                if (correctOption != null)
                {
                    CorrectOptionIndex = Question.Options.IndexOf(correctOption);
                }

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return RedirectToPage("QuizQuestions", new { quizId = QuizId });
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

                // Verify this instructor owns the question
                var ownsQuestion = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM QUIZ_QUESTIONS qq
                    JOIN QUIZZES q ON qq.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE qq.QUESTION_ID = @QuestionId AND c.CREATED_BY = @InstructorId",
                    new { QuestionId, InstructorId = userId });

                if (!ownsQuestion)
                {
                    return NotFound();
                }

                // Update question
                await connection.ExecuteAsync(@"
                    UPDATE QUIZ_QUESTIONS SET
                        QUESTION_TEXT = @QuestionText,
                        QUESTION_TYPE = @QuestionType,
                        POINTS = @Points,
                        SEQUENCE_NUMBER = @SequenceNumber
                    WHERE QUESTION_ID = @QuestionId",
                    new
                    {
                        Question.QuestionText,
                        Question.QuestionType,
                        Question.Points,
                        Question.SequenceNumber,
                        QuestionId
                    });

                // Delete existing options
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZ_OPTIONS 
                    WHERE QUESTION_ID = @QuestionId",
                    new { QuestionId });

                // Insert new options
                for (int i = 0; i < Question.Options.Count; i++)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO QUIZ_OPTIONS (
                            QUESTION_ID,
                            OPTION_TEXT,
                            IS_CORRECT
                        ) VALUES (
                            @QuestionId,
                            @OptionText,
                            @IsCorrect
                        )",
                        new
                        {
                            QuestionId,
                            OptionText = Question.Options[i].OptionText,
                            IsCorrect = i == CorrectOptionIndex
                        });
                }

                return RedirectToPage("QuizQuestions", new { quizId = QuizId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error updating question: " + ex.Message);
                return Page();
            }
        }

        private class QuizInfo
        {
            public int QuizId { get; set; }
            public required string Title { get; set; }
            public int ModuleId { get; set; }
        }
    }
}