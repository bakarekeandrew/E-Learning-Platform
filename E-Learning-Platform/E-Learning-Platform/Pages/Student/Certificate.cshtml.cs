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
using E_Learning_Platform.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Document = QuestPDF.Fluent.Document;
using E_Learning_Platform.Helpers;
using Colors = QuestPDF.Helpers.Colors;
using PdfConstants = E_Learning_Platform.Helpers.PdfConstants;

namespace E_Learning_Platform.Pages.Student
{
    public class CertificateModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CertificateModel> _logger;
        private readonly ICourseProgressService _progressService;
        private const decimal PASSING_GRADE_THRESHOLD = 0.5m; // 50% passing threshold

        // Properties for eligibility details
        public int TotalModules { get; set; }
        public int CompletedModules { get; set; }
        public int QuizAttemptCount { get; set; }
        public int PassedQuizzes { get; set; }
        public decimal AverageGrade { get; set; }
        public bool IsEligibleForCertificate { get; set; }
        public CertificateInfo? UserCertificate { get; set; }
        public List<AssignmentSubmission> Submissions { get; set; } = new();
        public List<UserProgress> Progress { get; set; } = new();
        public InstructorInfo Instructor { get; set; } = new();
        public TimeSpan CompletionTime { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime CompletionDate { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string UserFullName { get; set; } = string.Empty;
        public bool IsPrinting { get; set; }
        public string QrCodeUrl { get; set; } = string.Empty;
        public List<string> SkillsLearned { get; set; } = new();
        public string StudentName { get; set; } = string.Empty;
        public CourseInfo Course { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public int CourseId { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool Print { get; set; }

        public class CourseInfo
        {
            public int CourseId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Instructor { get; set; } = string.Empty;
            public string ThumbnailUrl { get; set; } = string.Empty;
            public DateTime CreatedDate { get; set; }
            public string Category { get; set; } = string.Empty;
            public decimal Progress { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        public class InstructorInfo
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string ProfileImage { get; set; } = string.Empty;
            public string Qualification { get; set; } = string.Empty;
        }

        public class UserProgress
        {
            public int ProgressId { get; set; }
            public int UserId { get; set; }
            public int ModuleId { get; set; }
            public string ModuleTitle { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime? LastAccessed { get; set; }
            public DateTime? CompletedOn { get; set; }
        }

        public class AssignmentSubmission
        {
            public int SubmissionId { get; set; }
            public int AssignmentId { get; set; }
            public int UserId { get; set; }
            public DateTime SubmittedOn { get; set; }
            public decimal? Grade { get; set; }
            public string Feedback { get; set; } = string.Empty;
        }

        public class UserVerification
        {
            public bool UserExists { get; set; }
            public string FullName { get; set; } = string.Empty;
        }

        public class CertificateInfo
        {
            public int CertificateId { get; set; }
            public int UserId { get; set; }
            public int CourseId { get; set; }
            public DateTime IssueDate { get; set; }
            public DateTime CompletionDate { get; set; }
            public string CertificateNumber { get; set; } = string.Empty;
            public string CertificateUrl { get; set; } = string.Empty;
            public string VerificationCode { get; set; } = string.Empty;
        }

        public CertificateModel(ILogger<CertificateModel> logger, ICourseProgressService progressService, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException(nameof(configuration));
            _logger.LogInformation("[Certificate] CertificateModel initialized");
        }

        public async Task<IActionResult> OnGetAsync(int courseId)
        {
            _logger.LogInformation("[Certificate] OnGetAsync called with courseId: {CourseId}", courseId);

            if (courseId <= 0)
            {
                _logger.LogWarning("[Certificate] Invalid courseId provided: {CourseId}", courseId);
                ErrorMessage = "Course ID is required.";
                return RedirectToPage("/Student/Courses");
            }

            CourseId = courseId;
            IsPrinting = Print;

            // Get user ID from session
            var userIdValue = HttpContext.Session.GetInt32("UserId");
            if (!userIdValue.HasValue)
            {
                _logger.LogError("[Certificate] Failed to get user ID from session");
                return RedirectToPage("/Login", new { returnUrl = $"/Student/Certificate/{courseId}" });
            }
            var userId = userIdValue.Value.ToString();
            _logger.LogInformation("[Certificate] Processing certificate request for User: {UserId}, Course: {CourseId}", userId, CourseId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogDebug("[Certificate] Database connection established successfully");

                // First verify the user exists with detailed error info
                var userInfo = await connection.QueryFirstOrDefaultAsync<UserVerification>(
                    @"SELECT 
                        CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END as UserExists,
                        MAX(FULL_NAME) as FullName
                    FROM USERS 
                    WHERE USER_ID = @UserId",
                    new { UserId = int.Parse(userId) });

                _logger.LogDebug("[Certificate] User verification results - Exists: {UserExists}, HasName: {HasName}", 
                    userInfo?.UserExists ?? false, !string.IsNullOrEmpty(userInfo?.FullName));

                if (userInfo == null || !userInfo.UserExists)
                {
                    _logger.LogWarning("[Certificate] User not found in database - UserId: {UserId}", userId);
                    ErrorMessage = "User not found. Please try logging in again.";
                    return RedirectToPage("/Login");
                }

                StudentName = userInfo.FullName;

                // Get course details first
                Course = await connection.QueryFirstOrDefaultAsync<CourseInfo>(
                    @"SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.DESCRIPTION AS Description,
                        u.FULL_NAME AS Instructor,
                        c.THUMBNAIL_URL AS ThumbnailUrl,
                        c.CREATION_DATE AS CreatedDate,
                        cat.NAME AS Category
                    FROM COURSES c
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    LEFT JOIN CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                    WHERE c.COURSE_ID = @CourseId",
                    new { CourseId });

                if (Course == null)
                {
                    _logger.LogWarning("[Certificate] Course not found for courseId: {CourseId}", CourseId);
                    ErrorMessage = "Course not found.";
                    return RedirectToPage("/Student/Courses");
                }

                // Check if certificate already exists
                UserCertificate = await connection.QueryFirstOrDefaultAsync<CertificateInfo>(
                    @"SELECT 
                        CERTIFICATE_ID AS CertificateId, 
                        USER_ID AS UserId, 
                        COURSE_ID AS CourseId,
                        ISSUE_DATE AS IssueDate, 
                        COMPLETION_DATE AS CompletionDate,
                        CERTIFICATE_NUMBER AS CertificateNumber,
                        CERTIFICATE_URL AS CertificateUrl, 
                        VERIFICATION_CODE AS VerificationCode
                    FROM CERTIFICATES 
                    WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = int.Parse(userId), CourseId });

                // Get detailed progress information
                var moduleProgress = await connection.QueryFirstOrDefaultAsync<(int total, int completed)>(
                    @"SELECT 
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) as total,
                        (SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump 
                         JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID 
                         WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId 
                         AND ump.STATUS = 'completed') as completed",
                    new { UserId = int.Parse(userId), CourseId });

                TotalModules = moduleProgress.total;
                CompletedModules = moduleProgress.completed;

                // Get quiz attempts and passing information
                var quizProgress = await connection.QueryFirstOrDefaultAsync<(int attempts, int passed)>(
                    @"SELECT 
                        COUNT(*) as attempts,
                        SUM(CASE WHEN qa.PASSED = 1 THEN 1 ELSE 0 END) as passed
                    FROM QUIZ_ATTEMPTS qa
                    JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    WHERE qa.USER_ID = @UserId AND m.COURSE_ID = @CourseId",
                    new { UserId = int.Parse(userId), CourseId });

                QuizAttemptCount = quizProgress.attempts;
                PassedQuizzes = quizProgress.passed;

                // Calculate average grade
                var avgGrade = await connection.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT AVG(CAST(s.GRADE as decimal(5,2)))
                    FROM ASSIGNMENT_SUBMISSIONS s
                    JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                    WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL",
                    new { UserId = int.Parse(userId), CourseId });

                AverageGrade = avgGrade ?? 0;

                // Check eligibility
                IsEligibleForCertificate = CompletedModules == TotalModules && 
                                          TotalModules > 0 && 
                                          PassedQuizzes > 0 && 
                                          AverageGrade >= PASSING_GRADE_THRESHOLD * 100;

                if (!IsEligibleForCertificate && UserCertificate == null)
                {
                    if (CompletedModules < TotalModules)
                    {
                        ErrorMessage = $"You need to complete all modules ({CompletedModules}/{TotalModules} completed).";
                    }
                    else if (PassedQuizzes == 0)
                    {
                        ErrorMessage = "You need to pass at least one quiz.";
                    }
                    else if (AverageGrade < PASSING_GRADE_THRESHOLD * 100)
                    {
                        ErrorMessage = $"Your average assignment grade ({AverageGrade:F1}%) needs to be at least {PASSING_GRADE_THRESHOLD * 100}%.";
                    }
                }

                _logger.LogInformation("[Certificate] Page loaded successfully - IsEligible: {IsEligible}, HasCertificate: {HasCertificate}, " +
                    "Modules: {CompletedModules}/{TotalModules}, Quizzes: {PassedQuizzes}/{QuizAttempts}, Grade: {Grade}%",
                    IsEligibleForCertificate, UserCertificate != null, CompletedModules, TotalModules, PassedQuizzes, QuizAttemptCount, AverageGrade);

                // Get completion date
                _logger.LogDebug("[Certificate] Retrieving course completion date");
                var completionDate = await connection.QueryFirstOrDefaultAsync<DateTime?>(
                    @"SELECT MAX(COMPLETED_ON) 
                      FROM USER_MODULE_PROGRESS ump 
                      JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID 
                      WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND ump.STATUS = 'completed'",
                    new { UserId = userId, CourseId = courseId });

                // If no completion date found, use current date
                CompletionDate = completionDate ?? DateTime.Now;
                _logger.LogInformation("[Certificate] Using completion date: {CompletionDate}", CompletionDate);

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Error processing certificate request for user {UserId}, course {CourseId}: {ErrorMessage}", 
                    userId, CourseId, ex.Message);
                ErrorMessage = "An error occurred while processing your certificate request.";
                return RedirectToPage("/Student/Courses");
            }
        }

        private async Task<(bool isEligible, EligibilityDetails details)> CheckEligibilityWithDetailsAsync(SqlConnection connection, string userId, int courseId)
        {
            try
            {
                _logger.LogInformation("[Certificate] Starting detailed eligibility check for user {UserId}, course {CourseId}", userId, courseId);
                
                var details = new EligibilityDetails();

                // Check module completion with details
                var moduleProgress = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                    SELECT 
                        (SELECT COUNT(*) FROM USER_MODULE_PROGRESS p 
                         JOIN MODULES m ON p.MODULE_ID = m.MODULE_ID 
                         WHERE p.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND p.STATUS = 'completed') as completed,
                        (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId) as total",
                    new { UserId = userId, CourseId = courseId });

                details.TotalModules = moduleProgress.total;
                details.CompletedModules = moduleProgress.completed;
                details.HasCompletedAllModules = moduleProgress.completed == moduleProgress.total && moduleProgress.total > 0;

                _logger.LogDebug("[Certificate] Module completion check - Completed: {Completed}/{Total}", 
                    moduleProgress.completed, moduleProgress.total);

                // Check assignment grades with details
                var assignmentGrades = await connection.QueryAsync<decimal>(
                    @"SELECT s.GRADE
                      FROM ASSIGNMENT_SUBMISSIONS s
                      JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID
                      WHERE s.USER_ID = @UserId AND a.COURSE_ID = @CourseId AND s.GRADE IS NOT NULL",
                    new { UserId = userId, CourseId = courseId });

                var grades = assignmentGrades.ToList();
                details.AssignmentCount = grades.Count;
                details.AverageGrade = grades.Any() ? grades.Average() : 0;
                details.HasPassingAssignmentGrade = grades.Any() && details.AverageGrade >= PASSING_GRADE_THRESHOLD;

                _logger.LogDebug("[Certificate] Assignment grade check - Count: {Count}, Average: {Average:F2}", 
                    details.AssignmentCount, details.AverageGrade);

                // Check quiz completion with details
                var quizResults = await connection.QueryFirstOrDefaultAsync<(int attempts, int passed)>(@"
                    SELECT 
                        COUNT(*) as attempts,
                        SUM(CASE WHEN qa.PASSED = 1 THEN 1 ELSE 0 END) as passed
                    FROM QUIZ_ATTEMPTS qa
                    JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                    JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                    WHERE qa.USER_ID = @UserId AND m.COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId = courseId });

                details.QuizAttempts = quizResults.attempts;
                details.PassedQuizzes = quizResults.passed;
                details.HasPassedQuiz = quizResults.passed > 0;

                _logger.LogDebug("[Certificate] Quiz completion check - Attempts: {Attempts}, Passed: {Passed}", 
                    quizResults.attempts, quizResults.passed);

                // Set appropriate error message based on checks
                if (!details.HasCompletedAllModules)
                {
                    details.ErrorMessage = $"You need to complete all modules ({details.CompletedModules}/{details.TotalModules} completed).";
                }
                else if (!details.HasPassingAssignmentGrade)
                {
                    details.ErrorMessage = $"Your average assignment grade ({details.AverageGrade:F2}%) needs to be at least 50%.";
                }
                else if (!details.HasPassedQuiz)
                {
                    details.ErrorMessage = "You need to pass at least one quiz.";
                }

                bool isEligible = details.HasCompletedAllModules && details.HasPassingAssignmentGrade && details.HasPassedQuiz;
                _logger.LogInformation("[Certificate] Eligibility check complete - IsEligible: {IsEligible}, Details: {@Details}", 
                    isEligible, details);

                return (isEligible, details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Error checking eligibility for user {UserId}", userId);
                return (false, new EligibilityDetails { ErrorMessage = "An error occurred while checking eligibility." });
            }
        }

        public class EligibilityDetails
        {
            public int TotalModules { get; set; }
            public int CompletedModules { get; set; }
            public bool HasCompletedAllModules { get; set; }
            public int AssignmentCount { get; set; }
            public decimal AverageGrade { get; set; }
            public bool HasPassingAssignmentGrade { get; set; }
            public int QuizAttempts { get; set; }
            public int PassedQuizzes { get; set; }
            public bool HasPassedQuiz { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
        }

        public async Task<IActionResult> OnPostGenerateCertificateAsync(int courseId)
        {
            _logger.LogInformation("[Certificate] Starting certificate generation for courseId: {CourseId}", courseId);
            
            var userIdValue = HttpContext.Session.GetInt32("UserId");
            if (!userIdValue.HasValue)
            {
                _logger.LogError("[Certificate] Failed to get user ID from session during certificate generation");
                return RedirectToPage("/Login");
            }
            var userId = userIdValue.Value;
            _logger.LogInformation("[Certificate] User ID retrieved from session: {UserId}", userId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("[Certificate] Opening database connection");
                await connection.OpenAsync();

                // Check if certificate already exists
                _logger.LogDebug("[Certificate] Checking for existing certificate");
                var existingCert = await connection.QueryFirstOrDefaultAsync<CertificateInfo>(
                    @"SELECT CERTIFICATE_ID AS CertificateId 
                      FROM CERTIFICATES 
                      WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId = courseId });

                if (existingCert != null)
                {
                    _logger.LogInformation("[Certificate] Certificate already exists for user {UserId}, course {CourseId}", userId, courseId);
                    return RedirectToPage(new { courseId });
                }

                // Generate verification code
                _logger.LogDebug("[Certificate] Generating verification code");
                string verificationCode = await GenerateVerificationCodeAsync();
                _logger.LogInformation("[Certificate] Generated verification code: {VerificationCode}", verificationCode);

                // Get completion date
                _logger.LogDebug("[Certificate] Retrieving course completion date");
                var completionDate = await connection.QueryFirstOrDefaultAsync<DateTime?>(
                    @"SELECT MAX(COMPLETED_ON) 
                      FROM USER_MODULE_PROGRESS ump 
                      JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID 
                      WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND ump.STATUS = 'completed'",
                    new { UserId = userId, CourseId = courseId });

                // If no completion date found, use current date
                if (!completionDate.HasValue)
                {
                    _logger.LogWarning("[Certificate] No completion date found, using current date");
                    completionDate = DateTime.Now;
                }

                var certificateUrl = $"/certificates/{userId}-{courseId}-{verificationCode}.pdf";
                _logger.LogInformation("[Certificate] Generated certificate URL: {CertificateUrl}", certificateUrl);

                // Insert certificate record
                _logger.LogDebug("[Certificate] Inserting certificate record into database");
                await connection.ExecuteAsync(
                    @"INSERT INTO CERTIFICATES (
                        USER_ID, COURSE_ID, ISSUE_DATE, COMPLETION_DATE,
                        CERTIFICATE_NUMBER, CERTIFICATE_URL, VERIFICATION_CODE
                    ) VALUES (
                        @UserId, @CourseId, @IssueDate, @CompletionDate,
                        @CertificateNumber, @CertificateUrl, @VerificationCode
                    )",
                    new { 
                        UserId = userId, 
                        CourseId = courseId, 
                        IssueDate = DateTime.Now,
                        CompletionDate = completionDate.Value,
                        CertificateNumber = $"CERT-{DateTime.Now:yyyyMMdd}-{userId}-{courseId}",
                        CertificateUrl = certificateUrl,
                        VerificationCode = verificationCode
                    });

                _logger.LogInformation("[Certificate] Certificate record created successfully for user {UserId}, course {CourseId}", userId, courseId);
                return RedirectToPage(new { courseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Error generating certificate for user {UserId}, course {CourseId}: {ErrorMessage}", 
                    userId, courseId, ex.Message);
                TempData["ErrorMessage"] = "An error occurred while generating your certificate. Please try again.";
                return RedirectToPage(new { courseId });
            }
        }

        private async Task<string> GenerateVerificationCodeAsync()
        {
            _logger.LogDebug("[Certificate] Starting verification code generation");
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[16];
            rng.GetBytes(bytes);
            var code = DateTime.Now.ToString("yyyyMMdd") + "-" + BitConverter.ToString(bytes).Replace("-", "").ToUpper();
            _logger.LogDebug("[Certificate] Generated verification code: {Code}", code);
            return code;
        }

        public async Task<IActionResult> OnGetDownloadPdfAsync(int? courseId)
        {
            try
            {
                _logger.LogInformation("[Certificate] Starting PDF download for courseId: {CourseId}", courseId);
                QuestPDF.Settings.License = LicenseType.Community;

                if (courseId == null)
                {
                    _logger.LogWarning("[Certificate] Course ID is null");
                    return NotFound("Course ID is required.");
                }

                CourseId = courseId.Value;
                string userId;
                if (HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                {
                    var userIdValue = HttpContext.Session.GetInt32("UserId");
                    if (!userIdValue.HasValue)
                    {
                        _logger.LogError("[Certificate] Failed to get user ID from session");
                        return RedirectToPage("/Login");
                    }
                    userId = userIdValue.Value.ToString();
                    _logger.LogInformation("[Certificate] User ID retrieved: {UserId}", userId);
                }
                else
                {
                    _logger.LogWarning("[Certificate] No user ID found in session");
                    return RedirectToPage("/Login");
                }

                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("[Certificate] Opening database connection");
                await connection.OpenAsync();

                // Get certificate details
                _logger.LogDebug("[Certificate] Retrieving certificate details");
                var cert = await connection.QueryFirstOrDefaultAsync<CertificateInfo>(
                    @"SELECT 
                        CERTIFICATE_ID AS CertificateId,
                        USER_ID AS UserId,
                        COURSE_ID AS CourseId,
                        ISSUE_DATE AS IssueDate,
                        COMPLETION_DATE AS CompletionDate,
                        CERTIFICATE_NUMBER AS CertificateNumber,
                        CERTIFICATE_URL AS CertificateUrl,
                        VERIFICATION_CODE AS VerificationCode
                    FROM CERTIFICATES 
                    WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = userId, CourseId });

                if (cert == null)
                {
                    _logger.LogWarning("[Certificate] Certificate not found for user {UserId}, course {CourseId}", userId, courseId);
                    return NotFound("Certificate not found.");
                }

                // Get user and course details
                _logger.LogDebug("[Certificate] Retrieving user and course details");
                var userFullName = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                    new { UserId = int.Parse(userId) });
                var courseTitle = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT TITLE FROM COURSES WHERE COURSE_ID = @CourseId",
                    new { CourseId });
                var instructorName = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT u.FULL_NAME FROM COURSES c JOIN USERS u ON c.CREATED_BY = u.USER_ID WHERE c.COURSE_ID = @CourseId",
                    new { CourseId });

                if (string.IsNullOrEmpty(userFullName) || string.IsNullOrEmpty(courseTitle) || string.IsNullOrEmpty(instructorName))
                {
                    _logger.LogError("[Certificate] Missing required data for certificate generation");
                    return BadRequest("Missing required data for certificate generation.");
                }

                // Generate PDF
                _logger.LogDebug("[Certificate] Starting PDF generation");
                var pdfStream = new MemoryStream();
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "logo.png");
                var certificatesDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "certificates");
                
                // Ensure certificates directory exists
                if (!Directory.Exists(certificatesDir))
                {
                    _logger.LogInformation("[Certificate] Creating certificates directory: {Dir}", certificatesDir);
                    Directory.CreateDirectory(certificatesDir);
                }
                
                byte[] logoBytes = null;
                if (System.IO.File.Exists(logoPath))
                {
                    _logger.LogDebug("[Certificate] Loading logo from: {LogoPath}", logoPath);
                    logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
                }
                else
                {
                    _logger.LogWarning("[Certificate] Logo file not found at: {LogoPath}", logoPath);
                }

                // Enable QuestPDF debugging
                QuestPDF.Settings.EnableDebugging = true;

                _logger.LogDebug("[Certificate] Configuring PDF document");
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PdfConstants.PdfSizes.LetterWidth, PdfConstants.PdfSizes.LetterHeight);
                        page.Margin(2, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(18));

                        page.Header().Column(col =>
                        {
                            // Logo section with reduced size
                            if (logoBytes != null)
                            {
                                col.Item().AlignCenter().Height(60).Image(logoBytes);
                                col.Item().Height(30); // Increased spacing after logo
                            }

                            // Title with adjusted spacing
                            col.Item().AlignCenter().Text("Certificate of Completion")
                               .FontSize(32).Bold().FontColor("#2c3e50");
                            col.Item().Height(30); // Increased spacing

                            // Student name section with better spacing
                            col.Item().AlignCenter().Text("This certifies that")
                               .FontSize(18).FontColor("#555");
                            col.Item().Height(10);
                            col.Item().AlignCenter().Text(userFullName)
                               .FontSize(28).Bold().FontColor("#2c3e50");
                            col.Item().Height(20);

                            // Course section with adjusted spacing
                            col.Item().AlignCenter().Text("has successfully completed the course")
                               .FontSize(18).FontColor("#555");
                            col.Item().Height(10);
                            col.Item().AlignCenter().Text(courseTitle)
                               .FontSize(24).Bold().FontColor("#2c3e50");
                            col.Item().Height(30);

                            // Dates section with better spacing
                            col.Item().AlignCenter().Text($"Completion Date: {CompletionDate:MMMM d, yyyy}")
                               .FontSize(16);
                            col.Item().Height(10);
                            col.Item().AlignCenter().Text($"Issue Date: {cert.IssueDate:MMMM d, yyyy}")
                               .FontSize(16);
                            col.Item().Height(30);

                            // Certificate details with adjusted spacing
                            col.Item().AlignCenter().Text($"Certificate Number: {cert.CertificateNumber}")
                               .FontSize(12).FontColor("#888");
                            col.Item().Height(5);
                            col.Item().AlignCenter().Text($"Verification Code: {cert.VerificationCode}")
                               .FontSize(12).FontColor("#888");
                        });

                        // Signatures section with better spacing
                        page.Footer().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Column(sigCol =>
                            {
                                sigCol.Item().Text("________________________")
                                     .FontSize(16).FontColor("#2c3e50");
                                sigCol.Item().Height(10);
                                sigCol.Item().Text("Instructor")
                                     .FontSize(12).FontColor("#555");
                                sigCol.Item().Height(5);
                                sigCol.Item().Text(instructorName)
                                     .FontSize(12).FontColor("#555");
                            });

                            row.RelativeItem().AlignCenter().Column(sigCol =>
                            {
                                sigCol.Item().Text("________________________")
                                     .FontSize(16).FontColor("#2c3e50");
                                sigCol.Item().Height(10);
                                sigCol.Item().Text("Platform")
                                     .FontSize(12).FontColor("#555");
                                sigCol.Item().Height(5);
                                sigCol.Item().Text("E-Learning Platform")
                                     .FontSize(12).FontColor("#555");
                            });
                        });
                    });
                }).GeneratePdf(pdfStream);

                _logger.LogInformation("[Certificate] PDF generated successfully");
                pdfStream.Position = 0;
                var sanitizedFileName = $"Certificate_{userFullName.Replace(" ", "_")}_{courseTitle.Replace(" ", "_")}.pdf";
                return File(pdfStream, "application/pdf", sanitizedFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Certificate] Error generating PDF certificate: {ErrorMessage}", ex.Message);
                TempData["ErrorMessage"] = "An error occurred while generating your certificate. Please try again later.";
                return RedirectToPage(new { courseId });
            }
        }
    }
}