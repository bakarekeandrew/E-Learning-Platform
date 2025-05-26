using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class QuizModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<QuizModel> _logger;

        public QuizModel(ILogger<QuizModel> logger, IConfiguration configuration)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;Initial Catalog=ONLINE_LEARNING_PLATFORM;Integrated Security=True;TrustServerCertificate=True";
            _logger = logger;
            // Initialize Quiz property with default values
            Quiz = new QuizViewModel
            {
                Title = string.Empty,
                Description = string.Empty,
                ModuleTitle = string.Empty,
                CourseTitle = string.Empty,
                Questions = new List<QuizQuestion>()
            };
        }

        [BindProperty]
        public QuizViewModel Quiz { get; set; }

        [FromRoute]
        public int Id { get; set; }

        [BindProperty]
        public int TimeRemaining { get; set; }

        [BindProperty]
        public int CurrentQuestionIndex { get; set; }

        public string ErrorMessage { get; set; }
        public int CurrentUserId { get; set; }
        public int ModuleId { get; set; }
        public int CourseId { get; set; }
        public bool QuizAlreadyPassed { get; set; }
        public int PreviousAttempts { get; set; }
        public QuizAttemptSummary LastAttempt { get; set; }
        public DateTime? QuizStartTime { get; set; }
        public bool ShowPreviousResults { get; set; }
        public int MaxAttemptsAllowed { get; set; }
        public int AttemptsRemaining { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (Id <= 0)
            {
                _logger.LogError("Invalid quiz ID: {QuizId}", Id);
                return NotFound("Invalid quiz ID");
            }

            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }

            CurrentUserId = BitConverter.ToInt32(userIdBytes, 0);

            try
            {
                // Log connection string (REMOVE IN PRODUCTION - for debugging only)
                _logger.LogDebug("Connecting with connection string: {ConnectionString}",
                    string.IsNullOrEmpty(_connectionString) ?
                    "Empty connection string" :
                    (_connectionString.Length > 10 ? _connectionString.Substring(0, 10) + "..." : _connectionString));

                // Check if connection string is null or empty
                if (string.IsNullOrEmpty(_connectionString))
                {
                    _logger.LogError("Connection string is null or empty");
                    ErrorMessage = "Database configuration error: Connection string is missing.";
                    return Page();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                try
                {
                    // Get quiz details
                    var quizData = await connection.QueryFirstOrDefaultAsync<QuizViewModel>(@"
                        SELECT 
                            q.QUIZ_ID AS QuizId,
                            q.TITLE AS Title,
                            q.DESCRIPTION AS Description,
                            q.TIME_LIMIT_MINUTES AS TimeLimitMinutes,
                            q.PASSING_SCORE AS PassingScore,
                            q.MAX_ATTEMPTS AS AttemptsAllowed,
                            m.MODULE_ID AS ModuleId,
                            m.TITLE AS ModuleTitle,
                            c.COURSE_ID AS CourseId,
                            c.TITLE AS CourseTitle
                        FROM QUIZZES q
                        JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                        JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                        WHERE q.QUIZ_ID = @Id",
                        new { Id });

                    // Check if quiz was found
                    if (quizData == null)
                    {
                        _logger.LogWarning("Quiz with ID {Id} not found", Id);
                        ErrorMessage = $"Quiz with ID {Id} not found.";
                        return Page();
                    }

                    // Assign quiz data to the Quiz property
                    Quiz = quizData;
                    // Ensure Questions collection is initialized
                    Quiz.Questions = new List<QuizQuestion>();

                    // Set module and course IDs from quiz data
                    ModuleId = Quiz.ModuleId;
                    CourseId = Quiz.CourseId;

                    // Store max attempts allowed
                    MaxAttemptsAllowed = Quiz.AttemptsAllowed ?? 0;

                    // Check if student is enrolled in this course
                    var isEnrolled = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                        new { UserId = CurrentUserId, CourseId = Quiz.CourseId });

                    if (!isEnrolled)
                    {
                        ErrorMessage = "You are not enrolled in this course.";
                        return Page();
                    }

                    // Get quiz questions with options
                    var questions = await connection.QueryAsync<QuizQuestion>(@"
                        SELECT 
                            qq.QUESTION_ID AS QuestionId,
                            qq.QUESTION_TEXT AS Text,
                            qq.QUESTION_TYPE AS QuestionType,
                            qq.POINTS AS Points,
                            qq.SEQUENCE_NUMBER AS SequenceNumber
                        FROM QUIZ_QUESTIONS qq
                        WHERE qq.QUIZ_ID = @Id
                        ORDER BY qq.SEQUENCE_NUMBER",
                        new { Id });

                    // Check if questions were returned and assign them to the Quiz
                    if (questions != null)
                    {
                        Quiz.Questions = questions.ToList();
                        _logger.LogInformation("Found {QuestionCount} questions for quiz {QuizId}",
                            Quiz.Questions.Count, Id);
                    }
                    else
                    {
                        Quiz.Questions = new List<QuizQuestion>();
                        _logger.LogWarning("No questions found for quiz {QuizId}", Id);
                    }

                    // Get options for each question
                    foreach (var question in Quiz.Questions)
                    {
                        // Initialize Options collection to prevent null reference
                        question.Options = new List<QuizOption>();

                        var options = await connection.QueryAsync<QuizOption>(@"
                            SELECT 
                                qo.OPTION_ID AS OptionId,
                                qo.OPTION_TEXT AS Text,
                                qo.IS_CORRECT AS IsCorrect
                            FROM QUIZ_OPTIONS qo
                            WHERE qo.QUESTION_ID = @QuestionId
                            ORDER BY qo.OPTION_ID",
                            new { QuestionId = question.QuestionId });

                        if (options != null && options.Any())
                        {
                            question.Options = options.ToList();
                        }
                        else
                        {
                            _logger.LogWarning("No options found for question {QuestionId}", question.QuestionId);
                        }
                    }

                    // Check previous attempts
                    PreviousAttempts = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @Id",
                        new { UserId = CurrentUserId, Id });

                    // Calculate attempts remaining
                    AttemptsRemaining = MaxAttemptsAllowed > 0 ? MaxAttemptsAllowed - PreviousAttempts : -1;

                    // Check if quiz is already passed
                    QuizAlreadyPassed = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @Id AND PASSED = 1",
                        new { UserId = CurrentUserId, Id });

                    // Get last attempt info
                    if (PreviousAttempts > 0)
                    {
                        LastAttempt = await connection.QueryFirstOrDefaultAsync<QuizAttemptSummary>(@"
                            SELECT TOP 1
                                ATTEMPT_ID AS AttemptId,
                                SCORE AS Score,
                                (SELECT COUNT(*) FROM QUIZ_QUESTIONS WHERE QUIZ_ID = @Id) AS TotalQuestions,
                                PASSED AS Passed,
                                START_TIME AS AttemptDate
                            FROM QUIZ_ATTEMPTS
                            WHERE USER_ID = @UserId AND QUIZ_ID = @Id
                            ORDER BY START_TIME DESC",
                            new { UserId = CurrentUserId, Id });

                        // Set flag to show previous results if requested
                        ShowPreviousResults = Request.Query.ContainsKey("showResults");
                    }

                    // Check if maximum attempts reached
                    if (MaxAttemptsAllowed > 0 && PreviousAttempts >= MaxAttemptsAllowed && !QuizAlreadyPassed)
                    {
                        ErrorMessage = "You have reached the maximum number of attempts allowed for this quiz.";
                        return Page();
                    }

                    // Check for an active quiz session
                    if (HttpContext.Session.TryGetValue("QuizStartTime_" + Id, out var quizStartTimeBytes))
                    {
                        var ticks = BitConverter.ToInt64(quizStartTimeBytes, 0);
                        QuizStartTime = new DateTime(ticks);

                        // If we have a time limit, check if quiz has expired
                        if (Quiz.TimeLimitMinutes.HasValue && Quiz.TimeLimitMinutes.Value > 0)
                        {
                            var elapsedTime = DateTime.Now - QuizStartTime.Value;
                            var timeLimit = TimeSpan.FromMinutes(Quiz.TimeLimitMinutes.Value);

                            if (elapsedTime > timeLimit)
                            {
                                // Quiz has expired - auto-submit
                                return await AutoSubmitQuizAsync();
                            }
                        }
                    }
                    else if (Quiz.TimeLimitMinutes.HasValue && Quiz.TimeLimitMinutes.Value > 0)
                    {
                        // Initialize quiz timer
                        QuizStartTime = DateTime.Now;
                        HttpContext.Session.Set("QuizStartTime_" + Id, BitConverter.GetBytes(QuizStartTime.Value.Ticks));
                    }

                    return Page();
                }
                catch (SqlException sqlEx)
                {
                    _logger.LogError("SQL error: {Error}", sqlEx.Message);
                    ErrorMessage = "Database error: " + sqlEx.Message;
                    return Page();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error loading quiz: {Error}", ex.Message);
                    ErrorMessage = "An error occurred while loading the quiz: " + ex.Message;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading quiz", ex);
                ErrorMessage = "An error occurred while loading the quiz: " + ex.Message;
                return Page();
            }
        }

        private async Task<IActionResult> AutoSubmitQuizAsync()
        {
            // This method handles auto-submission when time is up
            _logger.LogInformation("Auto-submitting quiz {QuizId} for user {UserId} due to time expiration", Id, CurrentUserId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get quiz details needed for submission
                var quizDetails = await connection.QueryFirstOrDefaultAsync(@"
                    SELECT 
                        QUIZ_ID AS QuizId, 
                        PASSING_SCORE AS PassingScore
                    FROM QUIZZES 
                    WHERE QUIZ_ID = @Id",
                    new { Id });

                if (quizDetails == null)
                {
                    ErrorMessage = "Failed to auto-submit quiz: Quiz not found.";
                    return Page();
                }

                // Calculate attempt number
                int attemptNumber = await connection.ExecuteScalarAsync<int>(
                    "SELECT ISNULL(MAX(ATTEMPT_NUMBER), 0) + 1 FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @QuizId",
                    new { UserId = CurrentUserId, QuizId = Id });

                // Get start time from session
                DateTime startTime = DateTime.Now;
                if (HttpContext.Session.TryGetValue("QuizStartTime_" + Id, out var quizStartTimeBytes))
                {
                    var ticks = BitConverter.ToInt64(quizStartTimeBytes, 0);
                    startTime = new DateTime(ticks);
                }

                // Record quiz attempt with score 0 (expired)
                int attemptId = await connection.ExecuteScalarAsync<int>(@"
                    INSERT INTO QUIZ_ATTEMPTS (
                        USER_ID, 
                        QUIZ_ID, 
                        ATTEMPT_NUMBER, 
                        START_TIME, 
                        END_TIME, 
                        SCORE, 
                        PASSED,
                        TIME_EXPIRED
                    )
                    OUTPUT INSERTED.ATTEMPT_ID
                    VALUES (
                        @UserId, 
                        @QuizId, 
                        @AttemptNumber, 
                        @StartTime, 
                        @EndTime, 
                        @Score, 
                        @Passed,
                        @TimeExpired
                    )",
                    new
                    {
                        UserId = CurrentUserId,
                        QuizId = Id,
                        AttemptNumber = attemptNumber,
                        StartTime = startTime,
                        EndTime = DateTime.Now,
                        Score = 0,
                        Passed = false,
                        TimeExpired = true
                    });

                // Clear quiz timer from session
                HttpContext.Session.Remove("QuizStartTime_" + Id);

                return RedirectToPage("/Student/Courses/QuizResult", new { attemptId });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error auto-submitting quiz", ex);
                ErrorMessage = "An error occurred while auto-submitting the quiz: " + ex.Message;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }

            CurrentUserId = BitConverter.ToInt32(userIdBytes, 0);

            try
            {
                // Validate Quiz object is not null
                if (Quiz == null)
                {
                    ErrorMessage = "Invalid quiz submission data.";
                    return Page();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify this quiz exists before proceeding
                var quizExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM QUIZZES WHERE QUIZ_ID = @QuizId",
                    new { QuizId = Quiz.QuizId });

                if (!quizExists)
                {
                    ErrorMessage = "Quiz not found.";
                    return Page();
                }

                // Get quiz max attempts
                var maxAttempts = await connection.ExecuteScalarAsync<int?>(
                    "SELECT MAX_ATTEMPTS FROM QUIZZES WHERE QUIZ_ID = @QuizId",
                    new { QuizId = Quiz.QuizId });

                // Check if user has reached max attempts
                if (maxAttempts.HasValue && maxAttempts.Value > 0)
                {
                    var attemptCount = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @QuizId",
                        new { UserId = CurrentUserId, QuizId = Quiz.QuizId });

                    if (attemptCount >= maxAttempts.Value)
                    {
                        ErrorMessage = "You have reached the maximum number of attempts allowed for this quiz.";
                        return Page();
                    }
                }

                // Get correct answers for scoring
                var questions = await connection.QueryAsync<QuestionWithAnswer>(@"
                    SELECT
                        qq.QUESTION_ID AS QuestionId,
                        qq.POINTS AS Points,
                        qq.QUESTION_TYPE AS QuestionType,
                        (SELECT OPTION_ID FROM QUIZ_OPTIONS WHERE QUESTION_ID = qq.QUESTION_ID AND IS_CORRECT = 1) AS CorrectOptionId
                    FROM QUIZ_QUESTIONS qq
                    WHERE qq.QUIZ_ID = @QuizId",
                    new { QuizId = Quiz.QuizId });

                // Ensure questions collection is not null
                if (questions == null)
                {
                    questions = new List<QuestionWithAnswer>();
                }

                // Calculate score
                int totalPoints = questions.Sum(q => q.Points);
                int earnedPoints = 0;
                int correctAnswers = 0;
                int totalAnswered = 0;

                // Initialize a dictionary to store question results for reporting
                Dictionary<int, bool> questionResults = new Dictionary<int, bool>();

                // Ensure Quiz.Questions is not null before attempting to access it
                if (Quiz.Questions != null)
                {
                    foreach (var question in questions)
                    {
                        // Find the student's answer for this question
                        var studentAnswer = Quiz.Questions.FirstOrDefault(q => q.QuestionId == question.QuestionId)?.SelectedOptionId;

                        if (studentAnswer.HasValue)
                        {
                            totalAnswered++;
                            bool isCorrect = studentAnswer.Value == question.CorrectOptionId;

                            // Store result
                            questionResults[question.QuestionId] = isCorrect;

                            if (isCorrect)
                            {
                                earnedPoints += question.Points;
                                correctAnswers++;
                            }
                        }
                        else
                        {
                            // Question was not answered
                            questionResults[question.QuestionId] = false;
                        }
                    }
                }

                // Calculate percentage score
                decimal scorePercentage = totalPoints > 0 ? ((decimal)earnedPoints / totalPoints * 100) : 0;

                // Get passing score
                var passingScore = await connection.ExecuteScalarAsync<decimal>(
                    @"SELECT PASSING_SCORE
                      FROM QUIZZES 
                      WHERE QUIZ_ID = @QuizId", 
                    new { QuizId = Quiz.QuizId });

                bool passed = scorePercentage >= passingScore;

                // Calculate attempt number
                int attemptNumber = await connection.ExecuteScalarAsync<int>(
                    "SELECT ISNULL(MAX(ATTEMPT_NUMBER), 0) + 1 FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @QuizId",
                    new { UserId = CurrentUserId, QuizId = Quiz.QuizId });

                // Determine start time (either from session or calculated from time remaining)
                DateTime startTime;
                if (HttpContext.Session.TryGetValue("QuizStartTime_" + Quiz.QuizId, out var quizStartTimeBytes))
                {
                    var ticks = BitConverter.ToInt64(quizStartTimeBytes, 0);
                    startTime = new DateTime(ticks);
                }
                else
                {
                    startTime = DateTime.Now.AddSeconds(-TimeRemaining);
                }

                // Check if time expired
                bool timeExpired = false;
                if (Quiz.TimeLimitMinutes.HasValue && Quiz.TimeLimitMinutes.Value > 0)
                {
                    var elapsedTime = DateTime.Now - startTime;
                    timeExpired = elapsedTime.TotalMinutes > Quiz.TimeLimitMinutes.Value;
                }

                // Record quiz attempt
                int attemptId = await connection.ExecuteScalarAsync<int>(@"
                    INSERT INTO QUIZ_ATTEMPTS (
                        USER_ID, 
                        QUIZ_ID, 
                        ATTEMPT_NUMBER, 
                        START_TIME, 
                        END_TIME, 
                        SCORE, 
                        TOTAL_POINTS,
                        PERCENTAGE_SCORE,
                        CORRECT_ANSWERS,
                        TOTAL_QUESTIONS,
                        ANSWERED_QUESTIONS,
                        PASSED,
                        TIME_EXPIRED
                    )
                    OUTPUT INSERTED.ATTEMPT_ID
                    VALUES (
                        @UserId, 
                        @QuizId, 
                        @AttemptNumber, 
                        @StartTime, 
                        @EndTime, 
                        @Score, 
                        @TotalPoints,
                        @PercentageScore,
                        @CorrectAnswers,
                        @TotalQuestions,
                        @AnsweredQuestions,
                        @Passed,
                        @TimeExpired
                    )",
                    new
                    {
                        UserId = CurrentUserId,
                        QuizId = Quiz.QuizId,
                        AttemptNumber = attemptNumber,
                        StartTime = startTime,
                        EndTime = DateTime.Now,
                        Score = earnedPoints,
                        TotalPoints = totalPoints,
                        PercentageScore = scorePercentage,
                        CorrectAnswers = correctAnswers,
                        TotalQuestions = questions.Count(),
                        AnsweredQuestions = totalAnswered,
                        Passed = passed,
                        TimeExpired = timeExpired
                    });

                // Store individual question responses
                if (Quiz.Questions != null)
                {
                    foreach (var question in Quiz.Questions.Where(q => q != null))
                    {
                        // Get correct option ID for this question
                        var correctOptionId = questions
                            .FirstOrDefault(q => q.QuestionId == question.QuestionId)?.CorrectOptionId;

                        // Determine if the answer is correct
                        bool isCorrect = question.SelectedOptionId.HasValue &&
                            correctOptionId.HasValue &&
                            question.SelectedOptionId.Value == correctOptionId.Value;

                        await connection.ExecuteAsync(@"
                            INSERT INTO QUIZ_RESPONSES (
                                ATTEMPT_ID, 
                                QUESTION_ID, 
                                SELECTED_OPTION_ID, 
                                IS_CORRECT)
                            VALUES (
                                @AttemptId, 
                                @QuestionId, 
                                @SelectedOptionId, 
                                @IsCorrect)",
                            new
                            {
                                AttemptId = attemptId,
                                QuestionId = question.QuestionId,
                                SelectedOptionId = question.SelectedOptionId,
                                IsCorrect = isCorrect
                            });
                    }
                }

                // If quiz is passed, update module progression
                if (passed)
                {
                    try
                    {
                        // First, get the module ID for this quiz
                        var moduleId = await connection.ExecuteScalarAsync<int>(
                            "SELECT MODULE_ID FROM QUIZZES WHERE QUIZ_ID = @QuizId",
                            new { QuizId = Quiz.QuizId });

                        // Check if there's already a completion record
                        var completionExists = await connection.ExecuteScalarAsync<bool>(
                            "SELECT COUNT(1) FROM MODULE_COMPLETIONS WHERE USER_ID = @UserId AND MODULE_ID = @ModuleId",
                            new { UserId = CurrentUserId, ModuleId = moduleId });

                        if (!completionExists)
                        {
                            // Insert new completion record
                            await connection.ExecuteAsync(
                                "INSERT INTO MODULE_COMPLETIONS (USER_ID, MODULE_ID, COMPLETION_DATE) VALUES (@UserId, @ModuleId, @CompletionDate)",
                                new { UserId = CurrentUserId, ModuleId = moduleId, CompletionDate = DateTime.Now });

                            // Log success
                            _logger.LogInformation("Module {ModuleId} marked as completed for user {UserId}", moduleId, CurrentUserId);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error but don't fail the quiz submission
                        _logger.LogError(ex, "Error updating module completion status for quiz {QuizId}", Quiz.QuizId);
                    }
                }

                // Clear quiz timer from session
                HttpContext.Session.Remove("QuizStartTime_" + Quiz.QuizId);

                return RedirectToPage("/Student/Courses/QuizResult", new { attemptId = attemptId });
            }
            catch (Exception ex)
            {
                _logger.LogError("Error submitting quiz", ex);
                ErrorMessage = "An error occurred while submitting the quiz: " + ex.Message;
                return Page();
            }
        }

        // Model classes
        public class QuizViewModel
        {
            public int QuizId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public int? TimeLimitMinutes { get; set; }
            public decimal PassingScore { get; set; }
            public int? AttemptsAllowed { get; set; }
            public int ModuleId { get; set; }
            public required string ModuleTitle { get; set; }
            public int CourseId { get; set; }
            public required string CourseTitle { get; set; }
            public List<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
        }

        public class QuizQuestion
        {
            public int QuestionId { get; set; }
            public required string Text { get; set; }
            public required string QuestionType { get; set; }
            public int Points { get; set; }
            public int SequenceNumber { get; set; }
            public List<QuizOption> Options { get; set; } = new List<QuizOption>();
            public int? SelectedOptionId { get; set; }
        }

        public class QuizOption
        {
            public int OptionId { get; set; }
            public required string Text { get; set; }
            public bool IsCorrect { get; set; }
            public bool IsSelected { get; set; }
        }

        public class QuestionWithAnswer
        {
            public int QuestionId { get; set; }
            public int Points { get; set; }
            public required string QuestionType { get; set; }
            public int CorrectOptionId { get; set; }
        }

        public class QuizAttemptSummary
        {
            public int AttemptId { get; set; }
            public int Score { get; set; }
            public int TotalQuestions { get; set; }
            public bool Passed { get; set; }
            public DateTime AttemptDate { get; set; }
        }
    }
}