using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Logging;
using E_Learning_Platform.Pages.Services;

namespace E_Learning_Platform.Pages.Student
{
    public class CoursesModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CoursesModel> _logger;
        private readonly CourseProgressService _progressService;

        public CoursesModel(ILogger<CoursesModel> logger, CourseProgressService progressService)
        {
            _logger = logger;
            _progressService = progressService;
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
            _logger.LogInformation("CoursesModel initialized with connection string: {ConnectionString}", _connectionString);
        }

        public List<EnrolledCourse> EnrolledCourses { get; set; } = new List<EnrolledCourse>();
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("Student Courses OnGetAsync started.");
            try
            {
                if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                {
                    _logger.LogWarning("User not authenticated - no UserId in session for Courses page.");
                    return RedirectToPage("/Login");
                }
                var userId = BitConverter.ToInt32(userIdBytes, 0);
                _logger.LogInformation("Loading courses for user {UserId}.", userId);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var enrolledCoursesData = await connection.QueryAsync<EnrolledCourse>(@"
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.THUMBNAIL_URL AS ThumbnailUrl,
                        u.FULL_NAME AS Instructor,
                        ce.ENROLLMENT_DATE AS EnrollmentDate,
                        ce.STATUS AS EnrollmentStatus, -- Renamed from Status to avoid clash with EnrolledCourse.Status property
                        ISNULL(cp.PROGRESS, 0) AS Progress -- Initial progress from COURSE_PROGRESS table
                    FROM COURSE_ENROLLMENTS ce
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    LEFT JOIN COURSE_PROGRESS cp ON ce.COURSE_ID = cp.COURSE_ID AND ce.USER_ID = cp.USER_ID
                    WHERE ce.USER_ID = @UserId
                    ORDER BY ce.ENROLLMENT_DATE DESC",
                    new { UserId = userId });

                EnrolledCourses = enrolledCoursesData.ToList();
                _logger.LogInformation("Found {Count} enrolled courses for user {UserId}.", EnrolledCourses.Count, userId);

                if (!EnrolledCourses.Any())
                {
                    // No need to log warning, just return Page. Message is handled in Razor view.
                    return Page();
                }

                foreach (var course in EnrolledCourses)
                {
                    try
                    {
                        _logger.LogInformation("Processing course {CourseId}: {Title} for detailed progress.", course.CourseId, course.Title);
                        
                        // Use CourseProgressService for consistency if available and suitable, or recalculate here.
                        // For this refactor, we'll recalculate to ensure the logic is embedded and clear.

                        // --- Calculate Total Items for this course ---
                        var totalModules = await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId",
                            new { CourseId = course.CourseId });

                        var totalQuizzesInCourse = await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(q.QUIZ_ID) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId",
                            new { CourseId = course.CourseId });

                        var totalAssignmentsInCourse = await connection.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId",
                            new { CourseId = course.CourseId });

                        float totalCountableItems = totalModules + totalQuizzesInCourse + totalAssignmentsInCourse;
                        _logger.LogDebug("Course {CId}: Total Items: Mod={TM}, Q={TQ}, A={TA}, Sum={Sum}", course.CourseId, totalModules, totalQuizzesInCourse, totalAssignmentsInCourse, totalCountableItems);

                        // --- Calculate Completed Items for this course by this user ---
                        var completedModulesCount = await connection.ExecuteScalarAsync<int>(@"
                            SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump
                            JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                            WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND ump.STATUS = 'completed'",
                            new { UserId = userId, CourseId = course.CourseId });

                        var passedQuizzesCount = await connection.ExecuteScalarAsync<int>(@"
                            SELECT COUNT(DISTINCT q.QUIZ_ID) 
                            FROM QUIZ_ATTEMPTS qa
                            JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                            JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                            WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1",
                            new { UserId = userId, CourseId = course.CourseId });

                        var gradedAssignmentsCount = await connection.ExecuteScalarAsync<int>(@"
                            SELECT COUNT(DISTINCT a.ASSIGNMENT_ID) 
                            FROM ASSIGNMENT_SUBMISSIONS s 
                            JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID 
                            WHERE a.COURSE_ID = @CourseId AND s.USER_ID = @UserId AND s.GRADE IS NOT NULL",
                            new { UserId = userId, CourseId = course.CourseId });

                        float completedCountableItems = completedModulesCount + passedQuizzesCount + gradedAssignmentsCount;
                         _logger.LogDebug("Course {CId}: Completed Items: Mod={CM}, Q={CQ}, A={CA}, Sum={SumC}", course.CourseId, completedModulesCount, passedQuizzesCount, gradedAssignmentsCount, completedCountableItems);
                        
                        course.Progress = 0; // Default to 0
                        if (totalCountableItems > 0)
                        {
                            course.Progress = Math.Round((decimal)(completedCountableItems / totalCountableItems) * 100, 2);
                        }
                        _logger.LogInformation("Course {CId}: Calculated Progress: {P}%", course.CourseId, course.Progress);

                        // Update COURSE_PROGRESS table as this is the authoritative calculation point when listing courses
                        await connection.ExecuteAsync(@"
                            MERGE COURSE_PROGRESS AS target
                            USING (SELECT @UserId AS USER_ID, @CourseId AS COURSE_ID) AS source
                            ON (target.USER_ID = source.USER_ID AND target.COURSE_ID = source.COURSE_ID)
                            WHEN MATCHED THEN
                                UPDATE SET PROGRESS = @Progress, LAST_ACCESSED = GETDATE()
                            WHEN NOT MATCHED THEN
                                INSERT (USER_ID, COURSE_ID, PROGRESS, LAST_ACCESSED)
                                VALUES (@UserId, @CourseId, @Progress, GETDATE());",
                            new { UserId = userId, CourseId = course.CourseId, Progress = course.Progress });

                        // --- Populate display properties ---
                        course.ModuleCount = totalModules;
                        course.CompletedModules = completedModulesCount;
                        course.ModuleProgress = course.ModuleCount > 0 ? Math.Round(((decimal)course.CompletedModules / course.ModuleCount) * 100, 2) : 0;

                        var hasStartedAnyModule = await connection.ExecuteScalarAsync<bool>(@"
                            SELECT CASE WHEN EXISTS (
                                SELECT 1 FROM USER_MODULE_PROGRESS ump
                                JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                                WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId
                            ) THEN 1 ELSE 0 END", 
                            new { UserId = userId, CourseId = course.CourseId });

                        if (course.Progress >= 99.9m) // Use a threshold for completion due to potential float inaccuracies if not rounding early
                            course.LearningState = "Completed";
                        else if (hasStartedAnyModule || course.Progress > 0) // If any module interaction or any progress at all
                            course.LearningState = "InProgress";
                        else
                            course.LearningState = "NotStarted";
                        
                        course.TotalQuizzes = totalQuizzesInCourse;
                        course.CompletedQuizzes = passedQuizzesCount;
                        course.QuizAverage = await connection.ExecuteScalarAsync<decimal?>(@"
                            SELECT AVG(CAST(qa.SCORE AS DECIMAL(5,2))) 
                            FROM QUIZ_ATTEMPTS qa
                            JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                            JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                            WHERE qa.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND qa.PASSED = 1") ?? 0;

                        course.TotalAssignments = totalAssignmentsInCourse;
                        course.CompletedAssignments = gradedAssignmentsCount;
                        course.AssignmentAverage = await connection.ExecuteScalarAsync<decimal?>(@"
                            SELECT AVG(CAST(s.GRADE AS DECIMAL(5,2))) 
                            FROM ASSIGNMENT_SUBMISSIONS s
                            JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                            WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL") ?? 0;

                         _logger.LogInformation("Course {CId}: State={LS}, ModProg={MP}%, QAvg={QA}, AAvg={AA}", 
                            course.CourseId, course.LearningState, course.ModuleProgress, course.QuizAverage, course.AssignmentAverage);

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calculating detailed progress for course {CourseId}: {ErrorMessage}", course.CourseId, ex.Message);
                        // Course will retain its initially loaded progress or 0 if calculation failed mid-way
                    }
                }

                _logger.LogInformation("Successfully loaded all courses and their progress for user {UserId}.", userId);
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading courses for student: {ErrorMessage}", ex.Message);
                ErrorMessage = $"An error occurred while loading your courses. Please try again. Details: {ex.Message}";
                return Page();
            }
        }

        public class EnrolledCourse
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string ThumbnailUrl { get; set; }
            public string Instructor { get; set; }
            public decimal Progress { get; set; }
            public DateTime EnrollmentDate { get; set; }
            public string Status { get; set; }

            // Progress tracking properties
            public int ModuleCount { get; set; }
            public int CompletedModules { get; set; }
            public decimal ModuleProgress { get; set; }
            public decimal QuizAverage { get; set; }
            public decimal AssignmentAverage { get; set; }
            public int CompletedQuizzes { get; set; }
            public int TotalQuizzes { get; set; }
            public int CompletedAssignments { get; set; }
            public int TotalAssignments { get; set; }
            public string LearningState { get; set; }
        }
    }
}