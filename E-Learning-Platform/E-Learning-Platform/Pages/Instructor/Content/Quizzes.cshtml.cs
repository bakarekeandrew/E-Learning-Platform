using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class QuizzesModel : InstructorPageModel
    {
        public List<Quiz> Quizzes { get; set; } = new List<Quiz>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public List<Module> Modules { get; set; } = new List<Module>();
        public int? SelectedCourseId { get; set; }
        public int? SelectedModuleId { get; set; }
        public string? CourseName { get; set; }
        public string? ModuleName { get; set; }

        // Input model for quiz editing
        public class EditQuizInput
        {
            public int QuizId { get; set; }
            public required string Title { get; set; }
            public string? Description { get; set; }
            public int TimeLimitMinutes { get; set; }
            public int PassingScore { get; set; }
        }

        // Input model for quiz creation
        public class CreateQuizInput
        {
            public required string Title { get; set; }
            public string? Description { get; set; }
            public int TimeLimitMinutes { get; set; }
            public int PassingScore { get; set; }
            public int ModuleId { get; set; }
        }

        [BindProperty]
        public EditQuizInput EditQuiz { get; set; } = null!;

        [BindProperty]
        public CreateQuizInput CreateQuiz { get; set; } = null!;

        public QuizzesModel(ILogger<QuizzesModel> logger, IConfiguration configuration)
            : base(logger, configuration)
        {
        }

        protected new async Task<IActionResult> ExecuteDbOperationAsync(Func<Task<IActionResult>> operation, string errorMessage)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, errorMessage);
                TempData["ErrorMessage"] = $"{errorMessage}: {ex.Message}";
                return RedirectToPage("/Error");
            }
        }

        public async Task<IActionResult> OnGetAsync(int? courseId = null, int? moduleId = null)
        {
            SelectedCourseId = courseId;
            SelectedModuleId = moduleId;
            
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get instructor's courses
                Courses = (await connection.QueryAsync<Course>(@"
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = c.COURSE_ID) AS ModuleCount
                    FROM COURSES c
                    WHERE c.CREATED_BY = @InstructorId
                    ORDER BY c.TITLE",
                    new { InstructorId = GetInstructorId() })).AsList();

                // If no course is selected and instructor has courses, select the first one
                SelectedCourseId = courseId ?? Courses.FirstOrDefault()?.CourseId;

                if (SelectedCourseId.HasValue)
                {
                    // Verify course ownership
                    var selectedCourse = Courses.FirstOrDefault(c => c.CourseId == SelectedCourseId);
                    if (selectedCourse == null)
                    {
                        return NotFound();
                    }

                    CourseName = selectedCourse.Title;

                    // Get modules for selected course
                    Modules = (await connection.QueryAsync<Module>(@"
                        SELECT 
                            m.MODULE_ID AS ModuleId,
                            m.TITLE AS Title,
                            (SELECT COUNT(*) FROM QUIZZES WHERE MODULE_ID = m.MODULE_ID) AS QuizCount
                        FROM MODULES m
                        WHERE m.COURSE_ID = @CourseId
                        ORDER BY m.SEQUENCE_NUMBER",
                        new { CourseId = SelectedCourseId })).AsList();

                    // If no module is selected and course has modules, select the first one
                    SelectedModuleId = moduleId ?? Modules.FirstOrDefault()?.ModuleId;

                    if (SelectedModuleId.HasValue)
                    {
                        // Verify module belongs to selected course
                        var selectedModule = Modules.FirstOrDefault(m => m.ModuleId == SelectedModuleId);
                        if (selectedModule == null)
                        {
                            return NotFound();
                        }

                        ModuleName = selectedModule.Title;

                        // Get quizzes for selected module with detailed information
                        Quizzes = (await connection.QueryAsync<Quiz>(@"
                            WITH QuizStats AS (
                                SELECT 
                                    q.QUIZ_ID,
                                    COUNT(DISTINCT qq.QUESTION_ID) AS QuestionCount,
                                    COUNT(DISTINCT qa.ATTEMPT_ID) AS AttemptCount,
                                    AVG(CAST(ISNULL(qa.SCORE, 0) AS FLOAT)) AS AverageScore
                                FROM QUIZZES q
                                LEFT JOIN QUIZ_QUESTIONS qq ON q.QUIZ_ID = qq.QUIZ_ID
                                LEFT JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID
                                GROUP BY q.QUIZ_ID
                            )
                            SELECT 
                                q.QUIZ_ID AS QuizId,
                                q.MODULE_ID AS ModuleId,
                                q.TITLE AS Title,
                                q.DESCRIPTION AS Description,
                                q.PASSING_SCORE AS PassingScore,
                                q.TIME_LIMIT_MINUTES AS TimeLimitMinutes,
                                q.MAX_ATTEMPTS AS MaxAttempts,
                                ISNULL(qs.QuestionCount, 0) AS QuestionCount,
                                ISNULL(qs.AttemptCount, 0) AS AttemptCount,
                                ISNULL(qs.AverageScore, 0) AS AverageScore
                            FROM QUIZZES q
                            LEFT JOIN QuizStats qs ON q.QUIZ_ID = qs.QUIZ_ID
                            WHERE q.MODULE_ID = @ModuleId
                            ORDER BY q.QUIZ_ID",
                            new { ModuleId = SelectedModuleId })).AsList();
                    }
                }

                return Page();
            }, "Error loading quizzes");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify quiz ownership
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        q.MODULE_ID AS ModuleId,
                        m.COURSE_ID AS CourseId
                    FROM QUIZZES q
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE q.QUIZ_ID = @QuizId AND c.CREATED_BY = @InstructorId",
                    new { QuizId = id, InstructorId = GetInstructorId() });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                // Delete related records first
                await connection.ExecuteAsync("DELETE FROM QUIZ_SUBMISSIONS WHERE QUIZ_ID = @QuizId", new { QuizId = id });
                await connection.ExecuteAsync("DELETE FROM QUIZ_OPTIONS WHERE QUESTION_ID IN (SELECT QUESTION_ID FROM QUIZ_QUESTIONS WHERE QUIZ_ID = @QuizId)", new { QuizId = id });
                await connection.ExecuteAsync("DELETE FROM QUIZ_QUESTIONS WHERE QUIZ_ID = @QuizId", new { QuizId = id });
                await connection.ExecuteAsync("DELETE FROM QUIZZES WHERE QUIZ_ID = @QuizId", new { QuizId = id });

                return RedirectToPage(new { courseId = quizInfo.CourseId, moduleId = quizInfo.ModuleId });
            }, "Error deleting quiz");
        }

        public async Task<IActionResult> OnPostEditQuizAsync()
        {
            if (!ModelState.IsValid)
            {
                return RedirectToPage();
            }

            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify quiz ownership
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        q.MODULE_ID AS ModuleId,
                        m.COURSE_ID AS CourseId
                    FROM QUIZZES q
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE q.QUIZ_ID = @QuizId AND c.CREATED_BY = @InstructorId",
                    new { QuizId = EditQuiz.QuizId, InstructorId = GetInstructorId() });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                // Update quiz
                await connection.ExecuteAsync(@"
                    UPDATE QUIZZES 
                    SET 
                        TITLE = @Title,
                        DESCRIPTION = @Description,
                        PASSING_SCORE = @PassingScore,
                        TIME_LIMIT_MINUTES = @TimeLimitMinutes,
                        MAX_ATTEMPTS = @MaxAttempts
                    WHERE QUIZ_ID = @QuizId",
                    new
                    {
                        EditQuiz.QuizId,
                        EditQuiz.Title,
                        EditQuiz.Description,
                        EditQuiz.PassingScore,
                        EditQuiz.TimeLimitMinutes,
                        MaxAttempts = 3 // Default value, you can make this configurable
                    });

                TempData["SuccessMessage"] = "Quiz updated successfully.";
                return RedirectToPage(new { courseId = quizInfo.CourseId, moduleId = quizInfo.ModuleId });
            }, "Error updating quiz");
        }

        public async Task<IActionResult> OnPostCreateQuizAsync()
        {
            if (!ModelState.IsValid)
            {
                return RedirectToPage();
            }

            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify module ownership
                var moduleInfo = await connection.QueryFirstOrDefaultAsync<ModuleInfo>(@"
                    SELECT 
                        m.MODULE_ID AS ModuleId,
                        m.COURSE_ID AS CourseId
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId = CreateQuiz.ModuleId, InstructorId = GetInstructorId() });

                if (moduleInfo == null)
                {
                    return NotFound();
                }

                // Create quiz
                var quizId = await connection.QuerySingleAsync<int>(@"
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
                        CreateQuiz.ModuleId,
                        CreateQuiz.Title,
                        CreateQuiz.Description,
                        CreateQuiz.PassingScore,
                        CreateQuiz.TimeLimitMinutes,
                        MaxAttempts = 3 // Default value, you can make this configurable
                    });

                TempData["SuccessMessage"] = "Quiz created successfully.";
                return RedirectToPage("/Instructor/Content/QuizQuestions", new { quizId });
            }, "Error creating quiz");
        }

        public class Quiz
        {
            public int QuizId { get; set; }
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public string? Description { get; set; }
            public int PassingScore { get; set; }
            public int TimeLimitMinutes { get; set; }
            public int MaxAttempts { get; set; }
            public int QuestionCount { get; set; }
            public int AttemptCount { get; set; }
            public double? AverageScore { get; set; }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
            public int ModuleCount { get; set; }
        }

        public class Module
        {
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public int QuizCount { get; set; }
        }

        private class QuizInfo
        {
            public int ModuleId { get; set; }
            public int CourseId { get; set; }
        }

        private class ModuleInfo
        {
            public int ModuleId { get; set; }
            public int CourseId { get; set; }
        }
    }
}