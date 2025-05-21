using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class AddQuestionModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        [BindProperty(SupportsGet = true)]
        public int QuizId { get; set; }

        public string QuizTitle { get; set; }
        public int ModuleId { get; set; }

        [BindProperty]
        public QuestionInput Question { get; set; } = new QuestionInput();

        [BindProperty]
        public int CorrectOptionIndex { get; set; } = 0;

        public class QuestionInput
        {
            [Required]
            public string QuestionText { get; set; }

            [Required]
            public string QuestionType { get; set; } = "multiple_choice";

            [Required]
            [Range(1, 100)]
            public int Points { get; set; } = 1;

            [Required]
            [Range(1, 100)]
            public int SequenceNumber { get; set; } = 1;

            public List<QuestionOption> Options { get; set; } = new List<QuestionOption>();
        }

        public class QuestionOption
        {
            public string OptionText { get; set; }
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

                // Set default sequence number to be after the last question
                var highestOrder = await connection.ExecuteScalarAsync<int?>(@"
                    SELECT MAX(SEQUENCE_NUMBER) 
                    FROM QUIZ_QUESTIONS 
                    WHERE QUIZ_ID = @QuizId",
                    new { QuizId });

                if (highestOrder.HasValue)
                {
                    Question.SequenceNumber = highestOrder.Value + 1;
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

                // Verify this instructor owns the quiz
                var ownsQuiz = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) 
                    FROM QUIZZES q
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE q.QUIZ_ID = @QuizId AND c.CREATED_BY = @InstructorId",
                    new { QuizId, InstructorId = userId });

                if (!ownsQuiz)
                {
                    return NotFound();
                }

                // For true/false questions, set default options
                if (Question.QuestionType == "true_false")
                {
                    Question.Options = new List<QuestionOption>
                    {
                        new QuestionOption { OptionText = "True" },
                        new QuestionOption { OptionText = "False" }
                    };
                    // Default correct answer to True
                    CorrectOptionIndex = 0;
                }

                // Insert question
                var questionId = await connection.ExecuteScalarAsync<int>(@"
                    INSERT INTO QUIZ_QUESTIONS (
                        QUIZ_ID,
                        QUESTION_TEXT,
                        QUESTION_TYPE,
                        POINTS,
                        SEQUENCE_NUMBER
                    ) VALUES (
                        @QuizId,
                        @QuestionText,
                        @QuestionType,
                        @Points,
                        @SequenceNumber
                    );
                    SELECT SCOPE_IDENTITY();",
                    new
                    {
                        QuizId,
                        Question.QuestionText,
                        Question.QuestionType,
                        Question.Points,
                        Question.SequenceNumber
                    });

                // Insert options
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
                            QuestionId = questionId,
                            OptionText = Question.Options[i].OptionText,
                            IsCorrect = i == CorrectOptionIndex
                        });
                }

                return RedirectToPage("QuizQuestions", new { quizId = QuizId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error adding question: " + ex.Message);
                return Page();
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