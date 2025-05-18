using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using E_Learning_Platform.Models;
using E_Learning_Platform.Pages.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Previewer;
using System.IO;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace E_Learning_Platform.Pages.Student
{
    public class CertificateModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CertificateModel> _logger;
        private readonly CourseProgressService _progressService;
        private const decimal PASSING_GRADE_THRESHOLD = 0.5m; // 50% passing threshold

        public CertificateModel(ILogger<CertificateModel> logger, CourseProgressService progressService, IConfiguration configuration)
        {
            _logger = logger;
            _progressService = progressService;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public Certificate UserCertificate { get; set; }
        public List<AssignmentSubmission> Submissions { get; set; }
        public List<UserProgress> Progress { get; set; }
        public List<QuizAttempt> QuizAttempts { get; set; }
        public Models.CourseDetails CourseDetails { get; set; }
        public InstructorInfo Instructor { get; set; }
        public double AverageGrade { get; set; }
        public TimeSpan CompletionTime { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CompletionDate { get; set; }
        public string ErrorMessage { get; set; }
        public string UserFullName { get; set; }
        public bool IsPrinting { get; set; }
        public string QrCodeUrl { get; set; }
        public string SkillsLearned { get; set; }
        public bool HasPassedQuiz { get; set; }
        public bool HasCompletedAllModules { get; set; }
        public bool HasPassingAssignmentGrade { get; set; }
        public bool IsEligibleForCertificate { get; set; }
        public string StudentName { get; set; }
        public CourseInfo Course { get; set; }

        [BindProperty(SupportsGet = true)]
        public int CourseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool Print { get; set; }

        public class Certificate
        {
            public int CertificateId { get; set; }
            public string UserId { get; set; }
            public int CourseId { get; set; }
            public DateTime IssueDate { get; set; }
            public string CertificateUrl { get; set; }
            public string VerificationCode { get; set; }
        }
        public async Task<IActionResult> OnGetAsync(int? courseId)
        {
            _logger.LogInformation("[Certificate] OnGetAsync called with courseId: {CourseId}", courseId);

            if (courseId == null)
            {
                _logger.LogWarning("[Certificate] CourseId parameter is null");
                return NotFound();
            }

            CourseId = courseId.Value;
            IsPrinting = Print;
            _logger.LogDebug("[Certificate] CourseId set to {CourseId}, IsPrinting: {IsPrinting}", CourseId, IsPrinting);

            string userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0).ToString();
                _logger.LogDebug("[Certificate] Retrieved UserId from session: {UserId}", userId);
            }
            else
            {
                _logger.LogWarning("[Certificate] UserId not found in session");
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("[Certificate] Opening database connection");
                await connection.OpenAsync();

                // Get user's full name for the certificate
                StudentName = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                    new { UserId = int.Parse(userId) });

                // Get course information
                Course = await connection.QueryFirstOrDefaultAsync<CourseInfo>(
                    @"SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.DESCRIPTION AS Description,
                        u.FULL_NAME AS Instructor,
                        c.THUMBNAIL_URL AS ThumbnailUrl,
                        c.CREATION_DATE AS CreatedDate,
                        cat.NAME AS Category,
                        ISNULL(cp.PROGRESS, 0) AS Progress,
                        ce.STATUS AS Status
                    FROM COURSES c
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    LEFT JOIN CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                    LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @UserId
                    LEFT JOIN COURSE_PROGRESS cp ON c.COURSE_ID = cp.COURSE_ID AND cp.USER_ID = @UserId
                    WHERE c.COURSE_ID = @CourseId",
                    new { CourseId, UserId = int.Parse(userId) });

                if (Course == null)
                {
                    return NotFound();
                }
                // Check if certificate already exists
                _logger.LogDebug("[Certificate] Checking for existing certificate for user {UserId} and course {CourseId}", userId, CourseId);
                UserCertificate = await connection.QueryFirstOrDefaultAsync<Certificate>(
                    @"SELECT CERTIFICATE_ID AS CertificateId, USER_ID AS UserId, COURSE_ID AS CourseId, 
                      ISSUE_DATE AS IssueDate, CERTIFICATE_URL AS CertificateUrl, VERIFICATION_CODE AS VerificationCode
                      FROM CERTIFICATES WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId });

                if (UserCertificate == null)
                {
                    _logger.LogInformation("[Certificate] No existing certificate found. Checking eligibility...");
                    // Check eligibility and generate certificate if eligible
                    var isEligible = await CheckEligibilityAsync(connection, userId, CourseId);
                    _logger.LogInformation("[Certificate] Eligibility check result: {IsEligible}", isEligible);
                    if (isEligible)
                    {
                        _logger.LogInformation("[Certificate] User is eligible. Generating certificate...");
                        await GenerateCertificateInternalAsync(userId);
                        // Reload the certificate data
                        return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
                    }
                }
                else
                {
                    _logger.LogInformation("[Certificate] Existing certificate found: {CertificateId}", UserCertificate.CertificateId);
                }

                // Get course details (fixed columns)
                _logger.LogDebug("[Certificate] Retrieving course details for courseId: {CourseId}", CourseId);
                CourseDetails = await connection.QueryFirstOrDefaultAsync<Models.CourseDetails>(@"
                    SELECT 
                        c.TITLE AS CourseTitle,
                        cat.NAME AS Category,
                        c.CREATION_DATE AS CreatedOn
                    FROM COURSES c
                    LEFT JOIN CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                    WHERE c.COURSE_ID = @CourseId",
                    new { CourseId });
                if (CourseDetails == null)
                {
                    _logger.LogWarning("[Certificate] Course not found for courseId: {CourseId}", CourseId);
                    return NotFound();
                }
                _logger.LogDebug("[Certificate] Course details retrieved: {CourseTitle}", CourseDetails.CourseTitle);

                // Get instructor information
                if (!string.IsNullOrEmpty(CourseDetails.CreatedBy))
                {
                    _logger.LogDebug("[Certificate] Retrieving instructor info for instructorId: {InstructorId}", CourseDetails.CreatedBy);
                    Instructor = await connection.QueryFirstOrDefaultAsync<InstructorInfo>(
                        @"SELECT u.USER_ID AS UserId, u.FULL_NAME AS FullName, 
                         u.PROFILE_IMAGE AS ProfileImage, u.QUALIFICATION AS Qualification
                         FROM USERS u WHERE u.USER_ID = @InstructorId",
                        new { InstructorId = int.Parse(CourseDetails.CreatedBy) });

                    if (Instructor != null)
                    {
                        _logger.LogDebug("[Certificate] Instructor info retrieved: {InstructorName}", Instructor.FullName);
                    }
                }

                // Get assignment submissions (latest graded per assignment, join through ASSIGNMENTS)
                _logger.LogDebug("[Certificate] Retrieving latest graded assignment submissions for user {UserId} and course {CourseId}", userId, CourseId);
                Submissions = (await connection.QueryAsync<AssignmentSubmission>(
                    @"WITH LatestGraded AS (
                        SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.ASSIGNMENT_ID ORDER BY s.SUBMITTED_ON DESC) AS rn
                        FROM ASSIGNMENT_SUBMISSIONS s
                        JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                        WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL
                    )
                    SELECT * FROM LatestGraded WHERE rn = 1",
                    new { UserId = userId, CourseId })).ToList();
                _logger.LogDebug("[Certificate] Retrieved {SubmissionCount} latest graded assignment submissions", Submissions.Count);

                // Get progress for course modules
                _logger.LogDebug("[Certificate] Retrieving module progress for user {UserId} and course {CourseId}", userId, CourseId);
                Progress = (await connection.QueryAsync<UserProgress>(
                    @"SELECT p.PROGRESS_ID AS ProgressId, p.USER_ID AS UserId, p.MODULE_ID AS ModuleId,
                      m.TITLE AS ModuleTitle, p.STATUS AS Status, p.LAST_ACCESSED AS LastAccessed,
                      p.COMPLETED_ON AS CompletedOn FROM USER_PROGRESS p JOIN MODULES m ON p.MODULE_ID = m.MODULE_ID
                      WHERE p.USER_ID = @UserId AND m.COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId })).ToList();
                _logger.LogDebug("[Certificate] Retrieved {ProgressCount} progress records", Progress.Count);

                // Get quiz attempts (best score and passed status per quiz, join through QUIZZES)
                _logger.LogDebug("[Certificate] Retrieving best quiz attempts for user {UserId} and course {CourseId}", userId, CourseId);
                QuizAttempts = (await connection.QueryAsync<QuizAttempt>(
                    @"WITH BestQuizScores AS (
                        SELECT a.*, ROW_NUMBER() OVER (PARTITION BY a.QUIZ_ID ORDER BY a.SCORE DESC) AS rn
                        FROM QUIZ_ATTEMPTS a
                        JOIN QUIZZES q ON a.QUIZ_ID = q.QUIZ_ID
                        JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                        WHERE a.USER_ID = @UserId AND m.COURSE_ID = @CourseId
                    )
                    SELECT * FROM BestQuizScores WHERE rn = 1",
                    new { UserId = userId, CourseId })).ToList();
                _logger.LogDebug("[Certificate] Retrieved {QuizAttemptCount} best quiz attempts", QuizAttempts.Count);

                // Calculate average grade
                var assignmentGrades = Submissions
                    .Where(s => s.Grade.HasValue)
                    .Select(s => s.Grade.Value);
                var assignmentMaxScores = await connection.QueryAsync<decimal>(
                    @"SELECT a.MAX_SCORE FROM ASSIGNMENTS a
                      WHERE a.COURSE_ID = @CourseId",
                    new { CourseId });
                decimal maxScoreTotal = assignmentMaxScores.Sum();
                decimal gradeTotal = assignmentGrades.Sum();
                AverageGrade = (maxScoreTotal > 0) ? (double)(gradeTotal / maxScoreTotal) * 100 : 0;
                _logger.LogDebug("[Certificate] Calculated average grade: {AverageGrade}", AverageGrade);

                // Check eligibility criteria
                HasPassingAssignmentGrade = assignmentGrades.Any() && assignmentGrades.Average() >= PASSING_GRADE_THRESHOLD;
                HasPassedQuiz = QuizAttempts.Any(q => q.Score.HasValue && q.Score.Value >= PASSING_GRADE_THRESHOLD);

                // Use USER_MODULE_PROGRESS for module completion
                var totalModulesCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId",
                    new { CourseId });
                var completedModulesCount = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump
                      JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                      WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND ump.STATUS = 'completed'",
                    new { UserId = userId, CourseId });
                HasCompletedAllModules = totalModulesCount > 0 && completedModulesCount == totalModulesCount;
                IsEligibleForCertificate = HasPassingAssignmentGrade && HasPassedQuiz && HasCompletedAllModules;

                _logger.LogDebug("[Certificate] Eligibility results - PassedAssignments: {HasPassingAssignmentGrade}, PassedQuiz: {HasPassedQuiz}, CompletedModules: {HasCompletedAllModules}",
                    HasPassingAssignmentGrade, HasPassedQuiz, HasCompletedAllModules);

