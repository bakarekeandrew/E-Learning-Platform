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
using System.Reflection.Metadata;

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

                // Calculate completion time
                var firstActivity = Progress
                    .Where(p => p.LastAccessed.HasValue)
                    .OrderBy(p => p.LastAccessed)
                    .FirstOrDefault()?.LastAccessed;

                var lastActivity = Progress
                    .Where(p => p.CompletedOn.HasValue)
                    .OrderByDescending(p => p.CompletedOn)
                    .FirstOrDefault()?.CompletedOn;

                if (firstActivity.HasValue && lastActivity.HasValue)
                {
                    StartDate = firstActivity.Value;
                    CompletionDate = lastActivity.Value;
                    CompletionTime = CompletionDate - StartDate;
                    _logger.LogDebug("[Certificate] Course duration: {CompletionTime} days", CompletionTime.TotalDays);
                }

                // Generate QR code if certificate exists
                if (UserCertificate != null)
                {
                    QrCodeUrl = $"/api/qrcode?data=https://yourlearningplatform.com/verify/{UserCertificate.VerificationCode}";
                    _logger.LogDebug("[Certificate] Generated QR code URL for verification");
                }

                // Set error message if not eligible
                if (!IsEligibleForCertificate)
                {
                    if (!HasCompletedAllModules)
                    {
                        ErrorMessage = "You need to complete all modules for this course to earn a certificate.";
                    }
                    else if (!HasPassingAssignmentGrade)
                    {
                        ErrorMessage = "Your average assignment grade needs to be at least 50% to earn a certificate.";
                    }
                    else if (!HasPassedQuiz)
                    {
                        ErrorMessage = "You need to pass at least one quiz to earn a certificate.";
                    }
                    else
                    {
                        ErrorMessage = "You haven't met all requirements to earn a certificate for this course.";
                    }
                    _logger.LogInformation("[Certificate] User not eligible for certificate: {ErrorMessage}", ErrorMessage);
                }

                _logger.LogInformation("[Certificate] Certificate page successfully prepared for user {UserId}", userId);
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Exception details: {ExceptionMessage}", ex.Message);
                ErrorMessage = $"An error occurred while processing your certificate. Details: {ex.Message}";
                return Page();
            }
        }

        private async Task<bool> CheckEligibilityAsync(SqlConnection connection, string userId, int courseId)
        {
            try
            {
                // Check module completion
                var moduleProgress = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                    SELECT 
                        (SELECT COUNT(*) FROM USER_MODULE_PROGRESS p 
                         JOIN MODULES m ON p.MODULE_ID = m.MODULE_ID 
                         WHERE p.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND p.STATUS = 'completed') as completed,
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) as total",
                    new { UserId = userId, CourseId = courseId });

                bool hasCompletedAllModules = moduleProgress.completed == moduleProgress.total && moduleProgress.total > 0;

                // Check assignment grades
                var assignmentAverage = await connection.QueryFirstOrDefaultAsync<decimal?>(@"
                    SELECT AVG(CAST(s.GRADE as decimal(5,2)))
                    FROM ASSIGNMENT_SUBMISSIONS s
                    JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                    WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL",
                    new { UserId = userId, CourseId = courseId });

                bool hasPassingAssignmentGrade = (assignmentAverage ?? 0) >= PASSING_GRADE_THRESHOLD;

                // Check quiz completion
                var hasPassedQuiz = await connection.QueryFirstOrDefaultAsync<bool>(@"
                    SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM QUIZ_ATTEMPTS qa
                        JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                        JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                        WHERE qa.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND qa.PASSED = 1
                    ) THEN 1 ELSE 0 END",
                    new { UserId = userId, CourseId = courseId });

                return hasCompletedAllModules && hasPassingAssignmentGrade && hasPassedQuiz;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Error checking certificate eligibility: {ErrorMessage}", ex.Message);
                return false;
            }
        }

        public async Task<IActionResult> OnPostGenerateCertificateAsync()
        {
            _logger.LogInformation("Certificate generation requested by user");

            string userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0).ToString();
                _logger.LogDebug("Retrieved UserId from session: {UserId}", userId);
            }

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("User not authenticated, redirecting to login");
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("Opening database connection");
                await connection.OpenAsync();

                // Check if certificate already exists
                _logger.LogDebug("Checking for existing certificate");
                var existingCertificate = await connection.QueryFirstOrDefaultAsync<Certificate>(
                    @"SELECT CERTIFICATE_ID AS CertificateId FROM CERTIFICATES WITH (NOLOCK)
                      WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId });

                if (existingCertificate != null)
                {
                    _logger.LogInformation("Certificate already exists, redirecting");
                    return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
                }

                // Check eligibility criteria
                _logger.LogDebug("Checking certificate eligibility criteria");

                // 1. Check module completion
                var progressResult = await connection.QueryFirstOrDefaultAsync<(int AllModules, int CompletedModules)>(
                    @"SELECT (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) AS AllModules,
                      (SELECT COUNT(*) FROM USER_PROGRESS p JOIN MODULES m ON p.MODULE_ID = m.MODULE_ID
                      WHERE p.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND p.STATUS = 'Completed') AS CompletedModules",
                    new { UserId = userId, CourseId });

                bool hasCompletedAllModules = progressResult.CompletedModules >= progressResult.AllModules && progressResult.AllModules > 0;
                _logger.LogDebug("Module completion: {Completed}/{Total}", progressResult.CompletedModules, progressResult.AllModules);

                if (!hasCompletedAllModules)
                {
                    _logger.LogInformation("User hasn't completed all modules");
                    TempData["ErrorMessage"] = "You must complete all modules before generating a certificate.";
                    return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
                }

                // 2. Check assignment grades
                var assignmentGrades = await connection.QueryAsync<decimal?>(
                    @"SELECT s.GRADE FROM ASSIGNMENT_SUBMISSIONS s
                      JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                      WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL",
                    new { UserId = userId, CourseId });

                var validGrades = assignmentGrades.Where(g => g.HasValue).Select(g => g.Value).ToList();
                bool hasPassingAssignmentGrade = validGrades.Any() && validGrades.Average() >= PASSING_GRADE_THRESHOLD;
                _logger.LogDebug("Assignment grades: {Count}, Average: {Average}, Passing: {IsPassing}",
                    validGrades.Count, validGrades.Any() ? validGrades.Average() : 0, hasPassingAssignmentGrade);

                if (!hasPassingAssignmentGrade)
                {
                    _logger.LogInformation("User doesn't have passing assignment grade");
                    TempData["ErrorMessage"] = "Your average assignment grade must be at least 50% to earn a certificate.";
                    return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
                }

                // 3. Check quiz passing
                var hasPassedQuiz = await connection.QueryFirstOrDefaultAsync<bool>(
                    @"SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM QUIZ_ATTEMPTS a JOIN QUIZZES q ON a.QUIZ_ID = q.QUIZ_ID
                        JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                        WHERE a.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND a.PASSED = 1
                      ) THEN 1 ELSE 0 END",
                    new { UserId = userId, CourseId });
                _logger.LogDebug("Has passed quiz: {HasPassedQuiz}", hasPassedQuiz);

                if (!hasPassedQuiz)
                {
                    _logger.LogInformation("User hasn't passed any quizzes");
                    TempData["ErrorMessage"] = "You must pass at least one quiz to earn a certificate.";
                    return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
                }

                // All conditions met, generate certificate
                _logger.LogInformation("All conditions met, generating certificate");
                await GenerateCertificateInternalAsync(userId);

                _logger.LogInformation("Certificate generated successfully, redirecting");
                return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating certificate for user {UserId}", userId);
                TempData["ErrorMessage"] = "An error occurred while generating your certificate. Please try again later.";
                return RedirectToPage("/Student/Certificate", new { courseId = CourseId });
            }
        }

        private async Task AutoGenerateCertificateAsync(string userId)
        {
            _logger.LogInformation("Auto-generating certificate for user {UserId}, course {CourseId}", userId, CourseId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("Opening database connection");
                await connection.OpenAsync();

                _logger.LogDebug("Checking for existing certificate");
                var existingCertificate = await connection.QueryFirstOrDefaultAsync<Certificate>(
                    @"SELECT CERTIFICATE_ID AS CertificateId FROM CERTIFICATES WITH (NOLOCK)
                      WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId });

                if (existingCertificate == null)
                {
                    _logger.LogInformation("No existing certificate found, generating new one");
                    await GenerateCertificateInternalAsync(userId);
                }
                else
                {
                    _logger.LogDebug("Certificate already exists, no action needed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-generating certificate for user {UserId}", userId);
            }
        }

        private async Task GenerateCertificateInternalAsync(string userId)
        {
            _logger.LogInformation("Generating certificate internally for user {UserId}", userId);

            using var connection = new SqlConnection(_connectionString);
            _logger.LogDebug("Opening database connection");
            await connection.OpenAsync();

            _logger.LogDebug("Generating verification code");
            string verificationCode = GenerateVerificationCode();
            string certificateUrl = $"/certificates/{userId}-{CourseId}-{verificationCode}.pdf";
            _logger.LogDebug("Certificate URL: {CertificateUrl}", certificateUrl);

            using var transaction = connection.BeginTransaction();
            try
            {
                _logger.LogDebug("Inserting certificate record");
                await connection.ExecuteAsync(
                    @"INSERT INTO CERTIFICATES (USER_ID, COURSE_ID, ISSUE_DATE, CERTIFICATE_URL, VERIFICATION_CODE)
                      VALUES (@UserId, @CourseId, @IssueDate, @CertificateUrl, @VerificationCode)",
                    new { UserId = userId, CourseId, IssueDate = DateTime.Now, CertificateUrl = certificateUrl, VerificationCode = verificationCode },
                    transaction);

                transaction.Commit();
                _logger.LogInformation("Certificate generated successfully with verification code: {VerificationCode}", verificationCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during certificate generation transaction");
                transaction.Rollback();
                throw;
            }
        }

        private string GenerateVerificationCode()
        {
            _logger.LogDebug("Generating secure verification code");
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var code = BitConverter.ToString(bytes).Replace("-", "").ToUpper();
            _logger.LogDebug("Generated verification code: {VerificationCode}", code);
            return code;
        }

        public async Task<IActionResult> OnGetDownloadPdfAsync(int? courseId)
        {
            // Set QuestPDF license - Community for free use if applicable
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            _logger.LogInformation("[Certificate] PDF download requested for courseId: {CourseId}", courseId);
            if (courseId == null)
                return NotFound();

            CourseId = courseId.Value;
            string userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
                userId = BitConverter.ToInt32(userIdBytes, 0).ToString();
            else
                return RedirectToPage("/Login");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            // Get certificate details
            var cert = await connection.QueryFirstOrDefaultAsync<Certificate>(
                @"SELECT CERTIFICATE_ID AS CertificateId, USER_ID AS UserId, COURSE_ID AS CourseId, 
          ISSUE_DATE AS IssueDate, CERTIFICATE_URL AS CertificateUrl, VERIFICATION_CODE AS VerificationCode
          FROM CERTIFICATES WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                new { UserId = userId, CourseId });

            if (cert == null)
                return NotFound();

            // Get user and course details
            var userFullName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                new { UserId = int.Parse(userId) });
            var courseTitle = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT TITLE FROM COURSES WHERE COURSE_ID = @CourseId",
                new { CourseId });
            var instructorName = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT u.FULL_NAME FROM COURSES c JOIN USERS u ON c.CREATED_BY = u.USER_ID WHERE c.COURSE_ID = @CourseId",
                new { CourseId });

            // Generate PDF
            var pdfStream = new MemoryStream();
            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "logo.png");
            byte[] logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(18));

                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Certificate of Completion").FontSize(32).Bold().FontColor("#2c3e50").AlignCenter();
                            col.Item().Text("").FontSize(8);
                        });
                    });

                    page.Content().Column(col =>
                    {
                        // Correct image handling
                        if (logoBytes != null)
                        {
                            col.Item().AlignCenter().Height(120).Image(logoBytes, ImageScaling.FitHeight);
                        }

                        col.Item().Text("").FontSize(10);
                        col.Item().AlignCenter().Text($"This certifies that").FontSize(18).FontColor("#555");
                        col.Item().AlignCenter().Text(userFullName).FontSize(28).Bold().FontColor("#2c3e50");
                        col.Item().AlignCenter().Text($"has successfully completed the course").FontSize(18).FontColor("#555");
                        col.Item().AlignCenter().Text(courseTitle).FontSize(24).Bold().FontColor("#2c3e50");
                        col.Item().Text("").FontSize(10);
                        col.Item().AlignCenter().Text($"Date of Completion: {cert.IssueDate:MMMM d, yyyy}").FontSize(16);
                        col.Item().AlignCenter().Text($"Certificate ID: {cert.VerificationCode}").FontSize(12).FontColor("#888");
                        col.Item().Text("").FontSize(10);
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Column(sigCol =>
                            {
                                sigCol.Item().Text("________________________").FontSize(16).FontColor("#2c3e50");
                                sigCol.Item().Text("Instructor").FontSize(12).FontColor("#555");
                                sigCol.Item().Text(instructorName).FontSize(12).FontColor("#555");
                            });
                            row.RelativeItem().AlignCenter().Column(sigCol =>
                            {
                                sigCol.Item().Text("________________________").FontSize(16).FontColor("#2c3e50");
                                sigCol.Item().Text("Platform").FontSize(12).FontColor("#555");
                                sigCol.Item().Text("E-Learning Platform").FontSize(12).FontColor("#555");
                            });
                        });
                    });
                });
            }).GeneratePdf(pdfStream);

            pdfStream.Position = 0;
            return File(pdfStream, "application/pdf", $"Certificate_{userFullName}_{courseTitle}.pdf");
        }

        public class InstructorInfo
        {
            public int UserId { get; set; }
            public string FullName { get; set; }
            public string ProfileImage { get; set; }
            public string Qualification { get; set; }
        }
    }
}