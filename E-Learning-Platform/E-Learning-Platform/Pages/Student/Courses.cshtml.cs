using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages.Student
{
    public class CoursesModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CoursesModel> _logger;

        public List<EnrolledCourse> EnrolledCourses { get; set; } = new();
        public string ErrorMessage { get; set; }

        public CoursesModel(IConfiguration configuration, ILogger<CoursesModel> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            _logger.LogInformation("[COURSES_PAGE] Starting OnGetAsync method");

            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                _logger.LogWarning("[COURSES_PAGE] User not authenticated, redirecting to login");
                return RedirectToPage("/Login");
            }

            _logger.LogInformation("[COURSES_PAGE] User {UserId} is authenticated, loading courses", userId.Value);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("[COURSES_PAGE] Database connection opened successfully");

                // Get enrolled courses with enhanced logging
                _logger.LogInformation("[COURSES_PAGE] Executing main course query for user {UserId}", userId.Value);
                var courses = await connection.QueryAsync<EnrolledCourse>(@"
                    SELECT 
                        c.COURSE_ID as CourseId,
                        c.TITLE as Title,
                        c.THUMBNAIL_URL as ThumbnailUrl,
                        u.FULL_NAME as Instructor,
                        ce.ENROLLMENT_DATE as EnrollmentDate,
                        ce.STATUS as Status,
                        ISNULL(cp.PROGRESS, 0) as Progress
                    FROM COURSE_ENROLLMENTS ce
                    JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    LEFT JOIN COURSE_PROGRESS cp ON ce.USER_ID = cp.USER_ID AND ce.COURSE_ID = cp.COURSE_ID
                    WHERE ce.USER_ID = @UserId
                    ORDER BY ce.ENROLLMENT_DATE DESC",
                    new { UserId = userId });

                EnrolledCourses = new List<EnrolledCourse>(courses);
                _logger.LogInformation("[COURSES_PAGE] Found {CourseCount} enrolled courses for user {UserId}", EnrolledCourses.Count, userId.Value);

                // Log each course details for debugging
                foreach (var course in EnrolledCourses)
                {
                    _logger.LogDebug("[COURSES_PAGE] Course {CourseId}: {Title}, Progress: {Progress}%, Status: {Status}",
                        course.CourseId, course.Title, course.Progress, course.Status);
                }

                // Calculate detailed progress for each course
                for (int i = 0; i < EnrolledCourses.Count; i++)
                {
                    var course = EnrolledCourses[i];
                    _logger.LogDebug("[COURSES_PAGE] Processing detailed progress for course {CourseId}: {Title}", course.CourseId, course.Title);

                    try
                    {
                        // Get module completion data
                        _logger.LogDebug("[COURSES_PAGE] Getting module progress for course {CourseId}", course.CourseId);
                        var moduleProgress = await connection.QueryFirstOrDefaultAsync<(int CompletedModules, int TotalModules)>(@"
                            SELECT 
                                (SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump 
                                 JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID 
                                 WHERE m.COURSE_ID = @CourseId AND ump.USER_ID = @UserId AND ump.STATUS = 'completed') AS CompletedModules,
                                (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS TotalModules",
                            new { CourseId = course.CourseId, UserId = userId });

                        course.CompletedModules = moduleProgress.CompletedModules;
                        course.ModuleCount = moduleProgress.TotalModules;

                        if (moduleProgress.TotalModules > 0)
                        {
                            course.Progress = (decimal)moduleProgress.CompletedModules / moduleProgress.TotalModules * 100;
                            course.ModuleProgress = course.Progress;
                        }

                        _logger.LogDebug("[COURSES_PAGE] Course {CourseId} module progress: {CompletedModules}/{TotalModules} = {Progress}%",
                            course.CourseId, course.CompletedModules, course.ModuleCount, course.Progress);

                        // Get quiz progress
                        _logger.LogDebug("[COURSES_PAGE] Getting quiz progress for course {CourseId}", course.CourseId);
                        var quizProgress = await connection.QueryFirstOrDefaultAsync<(int CompletedQuizzes, int TotalQuizzes, decimal AverageScore)>(@"
                            SELECT 
                                (SELECT COUNT(DISTINCT q.QUIZ_ID) 
                                 FROM QUIZZES q 
                                 JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID 
                                 JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID 
                                 WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1) AS CompletedQuizzes,
                                (SELECT COUNT(q.QUIZ_ID) 
                                 FROM QUIZZES q 
                                 JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID 
                                 WHERE m.COURSE_ID = @CourseId) AS TotalQuizzes,
                                ISNULL((SELECT AVG(CAST(qa.SCORE AS DECIMAL)) 
                                        FROM QUIZ_ATTEMPTS qa 
                                        JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID 
                                        JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID 
                                        WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId), 0) AS AverageScore",
                            new { CourseId = course.CourseId, UserId = userId });

                        course.CompletedQuizzes = quizProgress.CompletedQuizzes;
                        course.TotalQuizzes = quizProgress.TotalQuizzes;
                        course.QuizAverage = quizProgress.AverageScore;

                        _logger.LogDebug("[COURSES_PAGE] Course {CourseId} quiz progress: {CompletedQuizzes}/{TotalQuizzes}, Average: {AverageScore}%",
                            course.CourseId, course.CompletedQuizzes, course.TotalQuizzes, course.QuizAverage);

                        // Get assignment progress
                        _logger.LogDebug("[COURSES_PAGE] Getting assignment progress for course {CourseId}", course.CourseId);
                        var assignmentProgress = await connection.QueryFirstOrDefaultAsync<(int CompletedAssignments, int TotalAssignments, decimal AverageGrade)>(@"
                            SELECT 
                                (SELECT COUNT(DISTINCT a.ASSIGNMENT_ID) 
                                 FROM ASSIGNMENTS a 
                                 JOIN ASSIGNMENT_SUBMISSIONS asub ON a.ASSIGNMENT_ID = asub.ASSIGNMENT_ID 
                                 WHERE a.COURSE_ID = @CourseId AND asub.USER_ID = @UserId AND asub.GRADE IS NOT NULL) AS CompletedAssignments,
                                (SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId) AS TotalAssignments,
                                ISNULL((SELECT AVG(asub.GRADE) 
                                        FROM ASSIGNMENT_SUBMISSIONS asub 
                                        JOIN ASSIGNMENTS a ON asub.ASSIGNMENT_ID = a.ASSIGNMENT_ID 
                                        WHERE a.COURSE_ID = @CourseId AND asub.USER_ID = @UserId AND asub.GRADE IS NOT NULL), 0) AS AverageGrade",
                            new { CourseId = course.CourseId, UserId = userId });

                        course.CompletedAssignments = assignmentProgress.CompletedAssignments;
                        course.TotalAssignments = assignmentProgress.TotalAssignments;
                        course.AssignmentAverage = assignmentProgress.AverageGrade;

                        _logger.LogDebug("[COURSES_PAGE] Course {CourseId} assignment progress: {CompletedAssignments}/{TotalAssignments}, Average: {AverageGrade}%",
                            course.CourseId, course.CompletedAssignments, course.TotalAssignments, course.AssignmentAverage);

                        // Determine learning state
                        if (course.Progress >= 100)
                        {
                            course.LearningState = "Completed";
                        }
                        else if (course.Progress > 0)
                        {
                            course.LearningState = "InProgress";
                        }
                        else
                        {
                            course.LearningState = "NotStarted";
                        }

                        _logger.LogInformation("[COURSES_PAGE] Course {CourseId} final state: Progress={Progress}%, LearningState={LearningState}",
                            course.CourseId, course.Progress, course.LearningState);

                        // Determine certificate eligibility
                        bool hasCompletedAllModules = course.CompletedModules == course.ModuleCount && course.ModuleCount > 0;
                        bool hasPassingAssignmentGrade = course.AssignmentAverage >= 50;
                        bool hasPassedQuiz = course.CompletedQuizzes > 0;

                        course.IsEligibleForCertificate = hasCompletedAllModules && hasPassingAssignmentGrade && hasPassedQuiz;

                        if (!course.IsEligibleForCertificate)
                        {
                            if (!hasCompletedAllModules)
                            {
                                course.CertificateErrorMessage = $"Complete all modules ({course.CompletedModules}/{course.ModuleCount})";
                            }
                            else if (!hasPassingAssignmentGrade)
                            {
                                course.CertificateErrorMessage = $"Average assignment grade ({course.AssignmentAverage:F1}%) needs to be at least 50%";
                            }
                            else if (!hasPassedQuiz)
                            {
                                course.CertificateErrorMessage = "Pass at least one quiz";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[COURSES_PAGE] Error calculating detailed progress for course {CourseId}: {ErrorMessage}", course.CourseId, ex.Message);
                        // Set default values on error
                        course.LearningState = "NotStarted";
                        course.CompletedModules = 0;
                        course.ModuleCount = 0;
                        course.ModuleProgress = 0;
                        course.CompletedQuizzes = 0;
                        course.TotalQuizzes = 0;
                        course.QuizAverage = 0;
                        course.CompletedAssignments = 0;
                        course.TotalAssignments = 0;
                        course.AssignmentAverage = 0;
                        course.IsEligibleForCertificate = false;
                        course.CertificateErrorMessage = string.Empty;
                    }
                }

                _logger.LogInformation("[COURSES_PAGE] Successfully loaded all courses and their progress for user {UserId}. Final course count: {Count}", userId, EnrolledCourses.Count);

                // Log all courses with their navigation URLs for debugging
                foreach (var course in EnrolledCourses)
                {
                    var expectedUrl = $"/Student/Courses/View/{course.CourseId}";
                    _logger.LogInformation("[COURSES_PAGE] Course {CourseId} ({Title}) should navigate to: {ExpectedUrl}, LearningState: {LearningState}",
                        course.CourseId, course.Title, expectedUrl, course.LearningState);
                }

                return Page();
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "[COURSES_PAGE] Database error: {Error}", ex.Message);
                ErrorMessage = $"A database error occurred: {ex.Message}";
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COURSES_PAGE] Unexpected error: {Error}", ex.Message);
                ErrorMessage = $"An unexpected error occurred: {ex.Message}";
                return Page();
            }
        }

        public class EnrolledCourse
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
            public string? ThumbnailUrl { get; set; }
            public required string Instructor { get; set; }
            public decimal Progress { get; set; }
            public DateTime EnrollmentDate { get; set; }
            public required string Status { get; set; }
            public int CompletedModules { get; set; }
            public int ModuleCount { get; set; }
            public decimal ModuleProgress { get; set; }
            public int CompletedQuizzes { get; set; }
            public int TotalQuizzes { get; set; }
            public decimal QuizAverage { get; set; }
            public int CompletedAssignments { get; set; }
            public int TotalAssignments { get; set; }
            public decimal AssignmentAverage { get; set; }
            public string LearningState { get; set; } = "NotStarted";
            public bool IsEligibleForCertificate { get; set; }
            public string CertificateErrorMessage { get; set; } = string.Empty;
        }
    }
}