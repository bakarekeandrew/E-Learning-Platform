using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Services
{
    public class CourseProgressService
    {
        private readonly string _connectionString;
        private readonly ILogger<CourseProgressService> _logger;

        public CourseProgressService(string connectionString, ILogger<CourseProgressService> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("CourseProgressService initialized with connection string: {ConnectionString}", _connectionString);
        }

        public async Task<decimal> CalculateCourseProgress(int userId, int courseId)
        {
            _logger.LogInformation("Calculating course progress for user {UserId} and course {CourseId}", userId, courseId);
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("Database connection opened");

                // Get total counts
                var counts = await connection.QueryFirstOrDefaultAsync<CourseProgressCounts>(@"
                    SELECT 
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS ModuleCount,
                        (SELECT COUNT(*) FROM RESOURCES r JOIN MODULES m ON r.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS ResourceCount,
                        (SELECT COUNT(*) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS QuizCount,
                        (SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId) AS AssignmentCount",
                    new { CourseId = courseId });

                _logger.LogInformation("Total counts for course {CourseId}: Modules={ModuleCount}, Resources={ResourceCount}, Quizzes={QuizCount}, Assignments={AssignmentCount}",
                    courseId, counts.ModuleCount, counts.ResourceCount, counts.QuizCount, counts.AssignmentCount);

                // Get completed counts
                var completed = await connection.QueryFirstOrDefaultAsync<CourseProgressCounts>(@"
                    SELECT 
                        (SELECT COUNT(DISTINCT m.MODULE_ID) FROM MODULES m WHERE m.COURSE_ID = @CourseId AND EXISTS (
                            SELECT 1 FROM RESOURCES r 
                            LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON r.RESOURCE_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId
                            WHERE r.MODULE_ID = m.MODULE_ID
                        )) AS ModuleCount,
                        (SELECT COUNT(DISTINCT r.RESOURCE_ID) FROM RESOURCES r JOIN MODULES m ON r.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS ResourceCount,
                        (SELECT COUNT(DISTINCT q.QUIZ_ID) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1) AS QuizCount,
                        (SELECT COUNT(DISTINCT a.ASSIGNMENT_ID) FROM ASSIGNMENTS a JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID WHERE a.COURSE_ID = @CourseId AND s.USER_ID = @UserId) AS AssignmentCount",
                    new { UserId = userId, CourseId = courseId });

                _logger.LogInformation("Completed counts for user {UserId} and course {CourseId}: Modules={ModuleCount}, Resources={ResourceCount}, Quizzes={QuizCount}, Assignments={AssignmentCount}",
                    userId, courseId, completed.ModuleCount, completed.ResourceCount, completed.QuizCount, completed.AssignmentCount);

                // Calculate weighted progress
                decimal progress = 0;
                int totalWeight = 0;

                if (counts.ModuleCount > 0)
                {
                    var moduleProgress = ((decimal)completed.ModuleCount / counts.ModuleCount) * 30;
                    progress += moduleProgress;
                    totalWeight += 30;
                    _logger.LogDebug("Module progress: {Progress}% (weight: 30)", moduleProgress);
                }
                if (counts.ResourceCount > 0)
                {
                    var resourceProgress = ((decimal)completed.ResourceCount / counts.ResourceCount) * 20;
                    progress += resourceProgress;
                    totalWeight += 20;
                    _logger.LogDebug("Resource progress: {Progress}% (weight: 20)", resourceProgress);
                }
                if (counts.QuizCount > 0)
                {
                    var quizProgress = ((decimal)completed.QuizCount / counts.QuizCount) * 30;
                    progress += quizProgress;
                    totalWeight += 30;
                    _logger.LogDebug("Quiz progress: {Progress}% (weight: 30)", quizProgress);
                }
                if (counts.AssignmentCount > 0)
                {
                    var assignmentProgress = ((decimal)completed.AssignmentCount / counts.AssignmentCount) * 20;
                    progress += assignmentProgress;
                    totalWeight += 20;
                    _logger.LogDebug("Assignment progress: {Progress}% (weight: 20)", assignmentProgress);
                }

                decimal finalProgress = totalWeight > 0 ? Math.Round((progress / totalWeight) * 100, 2) : 0;
                _logger.LogInformation("Final progress for user {UserId} and course {CourseId}: {Progress}%", userId, courseId, finalProgress);

                // Update progress in database
                _logger.LogDebug("Updating progress in database for user {UserId} and course {CourseId}", userId, courseId);
                await connection.ExecuteAsync(@"
                    UPDATE COURSE_PROGRESS 
                    SET PROGRESS = @Progress, 
                        LAST_ACCESSED = GETDATE()
                    WHERE USER_ID = @UserId AND COURSE_ID = @CourseId
                    
                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO COURSE_PROGRESS (USER_ID, COURSE_ID, PROGRESS, LAST_ACCESSED)
                        VALUES (@UserId, @CourseId, @Progress, GETDATE())
                    END",
                    new { UserId = userId, CourseId = courseId, Progress = finalProgress });

                _logger.LogInformation("Progress updated successfully in database");
                return finalProgress;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating course progress for user {UserId} and course {CourseId}: {ErrorMessage}", userId, courseId, ex.Message);
                throw;
            }
        }

        public async Task<CourseProgressDetails> GetCourseProgressDetails(int userId, int courseId)
        {
            _logger.LogInformation("Getting course progress details for user {UserId} and course {CourseId}", userId, courseId);
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("Database connection opened");

                var details = await connection.QueryFirstOrDefaultAsync<CourseProgressDetails>(@"
                    SELECT 
                        cp.PROGRESS AS OverallProgress,
                        cp.LAST_ACCESSED AS LastAccessed,
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS TotalModules,
                        (SELECT COUNT(*) FROM RESOURCES r JOIN MODULES m ON r.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS TotalResources,
                        (SELECT COUNT(*) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS TotalQuizzes,
                        (SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId) AS TotalAssignments,
                        (SELECT COUNT(DISTINCT m.MODULE_ID) FROM MODULES m WHERE m.COURSE_ID = @CourseId AND EXISTS (
                            SELECT 1 FROM RESOURCES r 
                            LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON r.RESOURCE_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId
                            WHERE r.MODULE_ID = m.MODULE_ID
                        )) AS CompletedModules,
                        (SELECT COUNT(DISTINCT r.RESOURCE_ID) FROM RESOURCES r JOIN MODULES m ON r.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId) AS CompletedResources,
                        (SELECT COUNT(DISTINCT q.QUIZ_ID) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1) AS CompletedQuizzes,
                        (SELECT COUNT(DISTINCT a.ASSIGNMENT_ID) FROM ASSIGNMENTS a JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID WHERE a.COURSE_ID = @CourseId AND s.USER_ID = @UserId) AS CompletedAssignments
                    FROM COURSE_PROGRESS cp
                    WHERE cp.USER_ID = @UserId AND cp.COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId = courseId });

                if (details == null)
                {
                    _logger.LogWarning("No progress details found for user {UserId} and course {CourseId}", userId, courseId);
                    details = new CourseProgressDetails();
                }

                _logger.LogInformation("Progress details retrieved: Overall={OverallProgress}%, Modules={CompletedModules}/{TotalModules}, Resources={CompletedResources}/{TotalResources}, Quizzes={CompletedQuizzes}/{TotalQuizzes}, Assignments={CompletedAssignments}/{TotalAssignments}",
                    details.OverallProgress,
                    details.CompletedModules, details.TotalModules,
                    details.CompletedResources, details.TotalResources,
                    details.CompletedQuizzes, details.TotalQuizzes,
                    details.CompletedAssignments, details.TotalAssignments);

                return details;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting course progress details for user {UserId} and course {CourseId}: {ErrorMessage}", userId, courseId, ex.Message);
                throw;
            }
        }

        private class CourseProgressCounts
        {
            public int ModuleCount { get; set; }
            public int ResourceCount { get; set; }
            public int QuizCount { get; set; }
            public int AssignmentCount { get; set; }
        }
    }

    public class CourseProgressDetails
    {
        public decimal OverallProgress { get; set; }
        public DateTime LastAccessed { get; set; }
        public int TotalModules { get; set; }
        public int CompletedModules { get; set; }
        public int TotalResources { get; set; }
        public int CompletedResources { get; set; }
        public int TotalQuizzes { get; set; }
        public int CompletedQuizzes { get; set; }
        public int TotalAssignments { get; set; }
        public int CompletedAssignments { get; set; }

        public decimal ModuleProgress => TotalModules > 0 ? Math.Round((decimal)CompletedModules / TotalModules * 100, 1) : 0;
        public decimal ResourceProgress => TotalResources > 0 ? Math.Round((decimal)CompletedResources / TotalResources * 100, 1) : 0;
        public decimal QuizProgress => TotalQuizzes > 0 ? Math.Round((decimal)CompletedQuizzes / TotalQuizzes * 100, 1) : 0;
        public decimal AssignmentProgress => TotalAssignments > 0 ? Math.Round((decimal)CompletedAssignments / TotalAssignments * 100, 1) : 0;
    }
} 