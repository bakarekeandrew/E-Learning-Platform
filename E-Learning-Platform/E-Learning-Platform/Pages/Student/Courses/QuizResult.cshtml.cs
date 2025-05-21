using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class QuizResultModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<QuizResultModel> _logger;

        public QuizResultModel(ILogger<QuizResultModel> logger, IConfiguration configuration)
        {
            // Use the same connection string as in QuizModel
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;Initial Catalog=ONLINE_LEARNING_PLATFORM;Integrated Security=True;TrustServerCertificate=True";
            _logger = logger;
        }

        public QuizAttemptDetails AttemptDetails { get; set; }
        public List<QuestionResponse> QuestionResponses { get; set; } = new List<QuestionResponse>();
        public string ErrorMessage { get; set; }
        public int CurrentUserId { get; set; }
        public int CourseId { get; set; }
        public int ModuleId { get; set; }
        public int NextQuizId { get; set; }
        public bool HasNextQuiz { get; set; }
        public decimal ScorePercentage { get; set; }
        public decimal ModuleCompletionPercentage { get; set; }
        public bool IsModuleCompleted { get; set; }

        public async Task<IActionResult> OnGetAsync(int attemptId)
        {
            try
            {
                _logger.LogInformation("Loading quiz result for attempt ID: {AttemptId}", attemptId);

                if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                {
                    _logger.LogWarning("User not logged in, redirecting to login page");
                    return RedirectToPage("/Login");
                }

                CurrentUserId = BitConverter.ToInt32(userIdBytes, 0);
                _logger.LogInformation("User ID: {UserId}", CurrentUserId);

                using var connection = new SqlConnection(_connectionString);
                try
                {
                    await connection.OpenAsync();
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError(sqlEx, "Failed to open database connection");
                    ErrorMessage = "Database connection error: " + sqlEx.Message;
                    return Page();
                }

                // Get attempt details
                AttemptDetails = await connection.QueryFirstOrDefaultAsync<QuizAttemptDetails>(@"
                    SELECT 
                        qa.ATTEMPT_ID AS AttemptId,
                        qa.QUIZ_ID AS QuizId,
                        q.TITLE AS QuizTitle,
                        q.DESCRIPTION AS QuizDescription,
                        qa.START_TIME AS StartTime,
                        qa.END_TIME AS EndTime,
                        qa.SCORE AS Score,
                        (SELECT SUM(POINTS) FROM QUIZ_QUESTIONS WHERE QUIZ_ID = q.QUIZ_ID) AS TotalPoints,
                        (SELECT COUNT(*) FROM QUIZ_QUESTIONS WHERE QUIZ_ID = q.QUIZ_ID) AS TotalQuestions,
                        qa.PASSED AS Passed,
                        q.PASSING_SCORE AS PassingScore,
                        m.MODULE_ID AS ModuleId,
                        m.TITLE AS ModuleTitle,
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS CourseTitle
                    FROM QUIZ_ATTEMPTS qa
                    JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE qa.ATTEMPT_ID = @AttemptId AND qa.USER_ID = @UserId",
                    new { AttemptId = attemptId, UserId = CurrentUserId });

                if (AttemptDetails == null)
                {
                    _logger.LogWarning("Quiz attempt not found or does not belong to current user. AttemptId: {AttemptId}, UserId: {UserId}",
                        attemptId, CurrentUserId);
                    return NotFound("Quiz attempt not found.");
                }

                CourseId = AttemptDetails.CourseId;
                ModuleId = AttemptDetails.ModuleId;
                _logger.LogInformation("Course ID: {CourseId}, Module ID: {ModuleId}", CourseId, ModuleId);

                // Calculate score percentage
                ScorePercentage = AttemptDetails.TotalPoints > 0
                    ? (decimal)AttemptDetails.Score / AttemptDetails.TotalPoints * 100
                    : 0;

                // Get question responses with correct answers
                var responses = await connection.QueryAsync<QuestionResponseData>(@"
                    SELECT 
                        qr.QUESTION_ID AS QuestionId,
                        qq.QUESTION_TEXT AS QuestionText,
                        qq.POINTS AS Points,
                        qr.SELECTED_OPTION_ID AS SelectedOptionId,
                        so.OPTION_TEXT AS SelectedOptionText,
                        co.OPTION_ID AS CorrectOptionId,
                        co.OPTION_TEXT AS CorrectOptionText,
                        CASE WHEN qr.SELECTED_OPTION_ID = co.OPTION_ID THEN 1 ELSE 0 END AS IsCorrect
                    FROM QUIZ_RESPONSES qr
                    JOIN QUIZ_QUESTIONS qq ON qr.QUESTION_ID = qq.QUESTION_ID
                    LEFT JOIN QUIZ_OPTIONS so ON qr.SELECTED_OPTION_ID = so.OPTION_ID
                    JOIN QUIZ_OPTIONS co ON qq.QUESTION_ID = co.QUESTION_ID AND co.IS_CORRECT = 1
                    WHERE qr.ATTEMPT_ID = @AttemptId
                    ORDER BY qq.SEQUENCE_NUMBER",
                    new { AttemptId = attemptId });

                QuestionResponses = responses.Select((r, i) => new QuestionResponse
                {
                    QuestionNumber = i + 1,
                    QuestionId = r.QuestionId,
                    QuestionText = r.QuestionText,
                    Points = r.Points,
                    SelectedOptionId = r.SelectedOptionId,
                    SelectedOptionText = r.SelectedOptionText,
                    CorrectOptionId = r.CorrectOptionId,
                    CorrectOptionText = r.CorrectOptionText,
                    IsCorrect = r.IsCorrect
                }).ToList();

                _logger.LogInformation("Found {ResponseCount} question responses", QuestionResponses.Count);

                // Check if there's another quiz in this module
                NextQuizId = await connection.QueryFirstOrDefaultAsync<int?>(@"
                    SELECT TOP 1 q.QUIZ_ID
                    FROM QUIZZES q
                    WHERE q.MODULE_ID = @ModuleId
                    AND q.QUIZ_ID > @QuizId
                    ORDER BY q.QUIZ_ID",
                    new { ModuleId = ModuleId, QuizId = AttemptDetails.QuizId }) ?? 0;

                HasNextQuiz = NextQuizId > 0;
                _logger.LogInformation("Next Quiz ID: {NextQuizId}, Has Next Quiz: {HasNextQuiz}", NextQuizId, HasNextQuiz);

                // Get module completion status
                var moduleCompletionStatus = await connection.QueryFirstOrDefaultAsync<ModuleCompletionStatus>(@"
                    SELECT 
                        (SELECT COUNT(*) FROM RESOURCES WHERE MODULE_ID = @ModuleId) AS TotalResources,
                        (SELECT COUNT(*) FROM USER_PROGRESS 
                         WHERE USER_ID = @UserId AND MODULE_ID = @ModuleId 
                         AND RESOURCE_ID IS NOT NULL AND STATUS = 'completed') AS CompletedResources,
                        (SELECT COUNT(*) FROM QUIZZES WHERE MODULE_ID = @ModuleId) AS TotalQuizzes,
                        (SELECT COUNT(DISTINCT q.QUIZ_ID) FROM QUIZZES q
                         INNER JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID
                         WHERE q.MODULE_ID = @ModuleId AND qa.USER_ID = @UserId AND qa.PASSED = 1) AS PassedQuizzes
                    ",
                    new { ModuleId = ModuleId, UserId = CurrentUserId });

                if (moduleCompletionStatus != null)
                {
                    int totalItems = moduleCompletionStatus.TotalResources + moduleCompletionStatus.TotalQuizzes;
                    int completedItems = moduleCompletionStatus.CompletedResources + moduleCompletionStatus.PassedQuizzes;

                    ModuleCompletionPercentage = totalItems > 0
                        ? (decimal)completedItems / totalItems * 100
                        : 0;

                    IsModuleCompleted = (moduleCompletionStatus.CompletedResources >= moduleCompletionStatus.TotalResources) &&
                                       (moduleCompletionStatus.PassedQuizzes >= moduleCompletionStatus.TotalQuizzes);

                    _logger.LogInformation("Module completion: {CompletionPercentage}%, Is Completed: {IsCompleted}",
                        ModuleCompletionPercentage, IsModuleCompleted);
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading quiz results for attempt ID {AttemptId}", attemptId);
                ErrorMessage = "An error occurred while loading quiz results: " + ex.Message;
                return Page();
            }
        }

        // Model classes
        public class QuizAttemptDetails
        {
            public int AttemptId { get; set; }
            public int QuizId { get; set; }
            public required string QuizTitle { get; set; }
            public required string QuizDescription { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public int Score { get; set; }
            public int TotalPoints { get; set; }
            public int TotalQuestions { get; set; }
            public bool Passed { get; set; }
            public decimal PassingScore { get; set; }
            public int ModuleId { get; set; }
            public required string ModuleTitle { get; set; }
            public int CourseId { get; set; }
            public required string CourseTitle { get; set; }

            // Add AttemptDate property for view compatibility
            public DateTime AttemptDate => EndTime;
        }

        public class QuestionResponse
        {
            public int QuestionNumber { get; set; }
            public int QuestionId { get; set; }
            public required string QuestionText { get; set; }
            public int Points { get; set; }
            public int? SelectedOptionId { get; set; }
            public required string SelectedOptionText { get; set; }
            public int CorrectOptionId { get; set; }
            public required string CorrectOptionText { get; set; }
            public bool IsCorrect { get; set; }
        }

        private class QuestionResponseData
        {
            public int QuestionId { get; set; }
            public required string QuestionText { get; set; }
            public int Points { get; set; }
            public int? SelectedOptionId { get; set; }
            public required string SelectedOptionText { get; set; }
            public int CorrectOptionId { get; set; }
            public required string CorrectOptionText { get; set; }
            public bool IsCorrect { get; set; }
        }

        private class ModuleCompletionStatus
        {
            public int TotalResources { get; set; }
            public int CompletedResources { get; set; }
            public int TotalQuizzes { get; set; }
            public int PassedQuizzes { get; set; }
        }
    }
}