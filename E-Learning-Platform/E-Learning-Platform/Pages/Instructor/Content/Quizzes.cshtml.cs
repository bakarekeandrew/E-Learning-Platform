using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    public class QuizzesModel : PageModel
    {
        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                        "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                        "Integrated Security=True;" +
                                        "TrustServerCertificate=True";

        public List<Quiz> Quizzes { get; set; } = new List<Quiz>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public SelectList ModulesList { get; set; }

        public int? SelectedCourseId { get; set; }
        public int? SelectedModuleId { get; set; }

        public async Task<IActionResult> OnGetAsync(int? courseId = null, int? moduleId = null)
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

            SelectedCourseId = courseId;
            SelectedModuleId = moduleId;

            try
            {
                using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();

                // Get instructor's courses
                Courses = (await connection.QueryAsync<Course>(@"
                    SELECT 
                        COURSE_ID AS CourseId,
                        TITLE AS Title
                    FROM COURSES
                    WHERE CREATED_BY = @InstructorId
                    ORDER BY TITLE",
                    new { InstructorId = userId })).ToList();

                // If no course is selected and instructor has courses, select the first one
                if (!SelectedCourseId.HasValue && Courses.Count > 0)
                {
                    SelectedCourseId = Courses[0].CourseId;
                }

                if (SelectedCourseId.HasValue)
                {
                    // Get modules for selected course
                    var modules = await connection.QueryAsync<Module>(@"
                        SELECT 
                            MODULE_ID AS ModuleId,
                            TITLE AS Title
                        FROM MODULES
                        WHERE COURSE_ID = @CourseId
                        ORDER BY SEQUENCE_NUMBER",
                        new { CourseId = SelectedCourseId });

                    ModulesList = new SelectList(modules, "ModuleId", "Title");

                    // If no module is selected and course has modules, select the first one
                    if (!SelectedModuleId.HasValue && modules.Any())
                    {
                        SelectedModuleId = modules.First().ModuleId;
                    }

                    // Get quizzes for selected module
                    if (SelectedModuleId.HasValue)
                    {
                        Quizzes = (await connection.QueryAsync<Quiz>(@"
                            SELECT 
                                Q.QUIZ_ID AS QuizId,
                                Q.TITLE AS Title,
                                Q.DESCRIPTION AS Description,
                                Q.TIME_LIMIT_MINUTES AS TimeLimitMinutes,
                                Q.PASSING_SCORE AS PassingScore,
                                1 AS IsActive, /* Hard-coded since there's no IS_ACTIVE column */
                                (SELECT COUNT(*) FROM QUIZ_QUESTIONS WHERE QUIZ_ID = Q.QUIZ_ID) AS QuestionCount
                            FROM QUIZZES Q
                            WHERE Q.MODULE_ID = @ModuleId
                            ORDER BY Q.TITLE",
                            new { ModuleId = SelectedModuleId })).ToList();
                    }
                }

                return Page();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Database error occurred: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
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

                // Verify the quiz belongs to the instructor
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        Q.MODULE_ID AS ModuleId,
                        M.COURSE_ID AS CourseId
                    FROM QUIZZES Q
                    JOIN MODULES M ON Q.MODULE_ID = M.MODULE_ID
                    JOIN COURSES C ON M.COURSE_ID = C.COURSE_ID
                    WHERE Q.QUIZ_ID = @QuizId AND C.CREATED_BY = @InstructorId",
                    new { QuizId = id, InstructorId = userId });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                // Delete quiz options first
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZ_OPTIONS 
                    WHERE QUESTION_ID IN (
                        SELECT QUESTION_ID FROM QUIZ_QUESTIONS WHERE QUIZ_ID = @QuizId
                    )", new { QuizId = id });

                // Delete quiz questions
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZ_QUESTIONS 
                    WHERE QUIZ_ID = @QuizId", new { QuizId = id });

                // Delete the quiz
                await connection.ExecuteAsync(@"
                    DELETE FROM QUIZZES 
                    WHERE QUIZ_ID = @QuizId", new { QuizId = id });

                return RedirectToPage(new { courseId = quizInfo.CourseId, moduleId = quizInfo.ModuleId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error deleting quiz: " + ex.Message);
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostEditQuizAsync(int editQuizId, Quiz quiz, int moduleId)
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

                // Verify the quiz belongs to the instructor
                var quizInfo = await connection.QueryFirstOrDefaultAsync<QuizInfo>(@"
                    SELECT 
                        Q.MODULE_ID AS ModuleId,
                        M.COURSE_ID AS CourseId
                    FROM QUIZZES Q
                    JOIN MODULES M ON Q.MODULE_ID = M.MODULE_ID
                    JOIN COURSES C ON M.COURSE_ID = C.COURSE_ID
                    WHERE Q.QUIZ_ID = @QuizId AND C.CREATED_BY = @InstructorId",
                    new { QuizId = editQuizId, InstructorId = userId });

                if (quizInfo == null)
                {
                    return NotFound();
                }

                // Update the quiz
                await connection.ExecuteAsync(@"
                    UPDATE QUIZZES 
                    SET TITLE = @Title, 
                        DESCRIPTION = @Description, 
                        TIME_LIMIT_MINUTES = @TimeLimitMinutes, 
                        PASSING_SCORE = @PassingScore 
                    WHERE QUIZ_ID = @QuizId",
                    new
                    {
                        QuizId = editQuizId,
                        Title = quiz.Title,
                        Description = quiz.Description,
                        TimeLimitMinutes = quiz.TimeLimitMinutes,
                        PassingScore = quiz.PassingScore
                    });

                return RedirectToPage(new { courseId = quizInfo.CourseId, moduleId = quizInfo.ModuleId });
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", "Error updating quiz: " + ex.Message);
                return RedirectToPage(new { moduleId = moduleId });
            }
        }

        public class Quiz
        {
            public int QuizId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int TimeLimitMinutes { get; set; }
            public int PassingScore { get; set; }
            public bool IsActive { get; set; }
            public int QuestionCount { get; set; }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
        }

        public class Module
        {
            public int ModuleId { get; set; }
            public string Title { get; set; }
        }

        private class QuizInfo
        {
            public int ModuleId { get; set; }
            public int CourseId { get; set; }
        }
    }
}