using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Dapper;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Services
{
    public interface ICourseProgressService
    {
        Task<bool> IsEligibleForCertificate(int userId, int courseId);
        Task<decimal> GetCourseProgress(int userId, int courseId);
        Task<double> CalculateCourseProgressAsync(int userId, int courseId);
        Task<(double Progress, int CompletedModules, int TotalModules)> GetCourseProgressDetailsAsync(int userId, int courseId);
        Task UpdateModuleCompletionAsync(int userId, int moduleId, bool isCompleted);
        Task<bool> IsModuleCompletedAsync(int userId, int moduleId);
        Task<bool> HasCompletedAllModulesAsync(int userId, int courseId);
    }

    public class CourseProgressService : ICourseProgressService
    {
        private readonly string _connectionString;
        private readonly ILogger<CourseProgressService> _logger;

        public CourseProgressService(IConfiguration configuration, ILogger<CourseProgressService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> IsEligibleForCertificate(int userId, int courseId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var moduleCompletion = await connection.QueryFirstOrDefaultAsync<(int Total, int Completed)>(@"
                SELECT 
                    (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS Total,
                    (SELECT COUNT(*) FROM USER_MODULE_PROGRESS mc 
                     JOIN MODULES m ON mc.MODULE_ID = m.MODULE_ID 
                     WHERE m.COURSE_ID = @CourseId AND mc.USER_ID = @UserId AND mc.STATUS = 'completed') AS Completed",
                new { UserId = userId, CourseId = courseId });

            if (moduleCompletion.Total == 0 || moduleCompletion.Completed < moduleCompletion.Total)
                return false;

            var quizCompletion = await connection.QueryFirstOrDefaultAsync<(int Total, int Passed)>(@"
                SELECT 
                    (SELECT COUNT(*) FROM QUIZZES q 
                     JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID 
                     WHERE m.COURSE_ID = @CourseId) AS Total,
                    (SELECT COUNT(DISTINCT q.QUIZ_ID) FROM QUIZZES q 
                     JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID 
                     JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID 
                     WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1) AS Passed",
                new { UserId = userId, CourseId = courseId });

            if (quizCompletion.Total > 0 && quizCompletion.Passed < quizCompletion.Total)
                return false;

            var assignmentCompletion = await connection.QueryFirstOrDefaultAsync<(int Total, int Passed)>(@"
                SELECT 
                    (SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId) AS Total,
                    (SELECT COUNT(*) FROM ASSIGNMENTS a 
                     JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID 
                     WHERE a.COURSE_ID = @CourseId AND s.USER_ID = @UserId AND s.GRADE >= 60) AS Passed",
                new { UserId = userId, CourseId = courseId });

            if (assignmentCompletion.Total > 0 && assignmentCompletion.Passed < assignmentCompletion.Total)
                return false;

            return true;
        }

        public async Task<decimal> GetCourseProgress(int userId, int courseId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var progress = await connection.QueryFirstOrDefaultAsync<decimal>(@"
                SELECT ISNULL(PROGRESS, 0) 
                FROM COURSE_PROGRESS 
                WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                new { UserId = userId, CourseId = courseId });

            return progress;
        }

        public async Task<double> CalculateCourseProgressAsync(int userId, int courseId)
        {
            var progress = await GetCourseProgress(userId, courseId);
            return (double)progress;
        }

        public async Task<(double Progress, int CompletedModules, int TotalModules)> GetCourseProgressDetailsAsync(int userId, int courseId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var result = await connection.QueryFirstOrDefaultAsync<(int Completed, int Total)>(@"
                SELECT 
                    (SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump 
                     JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID 
                     WHERE m.COURSE_ID = @CourseId AND ump.USER_ID = @UserId AND ump.STATUS = 'completed') AS Completed,
                    (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS Total",
                new { UserId = userId, CourseId = courseId });

            double progress = result.Total > 0 ? (double)result.Completed / result.Total * 100 : 0;
            return (progress, result.Completed, result.Total);
        }

        public async Task UpdateModuleCompletionAsync(int userId, int moduleId, bool isCompleted)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync(@"
                UPDATE USER_MODULE_PROGRESS 
                SET STATUS = @Status,
                    COMPLETION_DATE = CASE WHEN @IsCompleted = 1 THEN GETDATE() ELSE NULL END
                WHERE USER_ID = @UserId AND MODULE_ID = @ModuleId
                
                IF @@ROWCOUNT = 0 AND @IsCompleted = 1
                BEGIN
                    INSERT INTO USER_MODULE_PROGRESS (USER_ID, MODULE_ID, STATUS, COMPLETION_DATE)
                    VALUES (@UserId, @ModuleId, @Status, GETDATE())
                END",
                new { 
                    UserId = userId, 
                    ModuleId = moduleId, 
                    IsCompleted = isCompleted,
                    Status = isCompleted ? "completed" : "in_progress"
                });
        }

        public async Task<bool> IsModuleCompletedAsync(int userId, int moduleId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var status = await connection.QueryFirstOrDefaultAsync<string>(@"
                SELECT STATUS
                FROM USER_MODULE_PROGRESS
                WHERE USER_ID = @UserId AND MODULE_ID = @ModuleId",
                new { UserId = userId, ModuleId = moduleId });

            return status == "completed";
        }

        public async Task<bool> HasCompletedAllModulesAsync(int userId, int courseId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var result = await connection.QueryFirstOrDefaultAsync<(int Total, int Completed)>(@"
                SELECT 
                    (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS Total,
                    (SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump
                     JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                     WHERE m.COURSE_ID = @CourseId 
                     AND ump.USER_ID = @UserId 
                     AND ump.STATUS = 'completed') AS Completed",
                new { UserId = userId, CourseId = courseId });

            return result.Total > 0 && result.Total == result.Completed;
        }
    }
} 