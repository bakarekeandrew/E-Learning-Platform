using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class CourseDebuggerModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CourseDebuggerModel> _logger;

        public CourseDebuggerModel(ILogger<CourseDebuggerModel> logger)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;Initial Catalog=ONLINE_LEARNING_PLATFORM;Integrated Security=True;TrustServerCertificate=True";
            _logger = logger;
        }

        public class DebugInfo
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        public int CourseId { get; set; }
        public int UserId { get; set; }
        public List<DebugInfo> CourseData { get; set; } = new List<DebugInfo>();
        public List<DebugInfo> ModuleData { get; set; } = new List<DebugInfo>();
        public List<DebugInfo> AssignmentData { get; set; } = new List<DebugInfo>();
        public List<DebugInfo> ResourceData { get; set; } = new List<DebugInfo>();
        public List<DebugInfo> UserProgressData { get; set; } = new List<DebugInfo>();
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            CourseId = id;

            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }

            UserId = BitConverter.ToInt32(userIdBytes, 0);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Test connection
                CourseData.Add(new DebugInfo
                {
                    Name = "Database Connection",
                    Value = "Success"
                });

                // Course data
                var course = await connection.QueryFirstOrDefaultAsync(@"
                    SELECT 
                        COURSE_ID, TITLE, DESCRIPTION, THUMBNAIL_URL, CREATED_BY,
                        CREATION_DATE
                    FROM COURSES WHERE COURSE_ID = @CourseId",
                    new { CourseId = id });

                if (course == null)
                {
                    CourseData.Add(new DebugInfo { Name = "Course Found", Value = "No - Course doesn't exist" });
                }
                else
                {
                    CourseData.Add(new DebugInfo { Name = "Course Found", Value = "Yes" });
                    CourseData.Add(new DebugInfo { Name = "Course Title", Value = course.TITLE?.ToString() ?? "NULL" });
                    CourseData.Add(new DebugInfo { Name = "Created By", Value = course.CREATED_BY?.ToString() ?? "NULL" });

                    // Check enrollment
                    var enrollment = await connection.QueryFirstOrDefaultAsync(@"
                        SELECT ENROLLMENT_ID, STATUS, ENROLLMENT_DATE
                        FROM COURSE_ENROLLMENTS 
                        WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                        new { UserId, CourseId = id });

                    if (enrollment == null)
                    {
                        CourseData.Add(new DebugInfo { Name = "User Enrolled", Value = "No - Not enrolled" });
                    }
                    else
                    {
                        CourseData.Add(new DebugInfo { Name = "User Enrolled", Value = "Yes" });
                        CourseData.Add(new DebugInfo { Name = "Enrollment Status", Value = enrollment.STATUS?.ToString() ?? "NULL" });
                        CourseData.Add(new DebugInfo { Name = "Enrollment Date", Value = enrollment.ENROLLMENT_DATE.ToString() });
                    }
                }

                // Modules data
                var modules = await connection.QueryAsync(@"
                    SELECT MODULE_ID, TITLE, DESCRIPTION, SEQUENCE_NUMBER
                    FROM MODULES
                    WHERE COURSE_ID = @CourseId
                    ORDER BY SEQUENCE_NUMBER",
                    new { CourseId = id });

                int moduleCount = 0;
                ModuleData.Add(new DebugInfo { Name = "Module Count", Value = modules.Count().ToString() });

                foreach (var module in modules)
                {
                    moduleCount++;
                    ModuleData.Add(new DebugInfo
                    {
                        Name = $"Module {moduleCount}",
                        Value = $"ID: {module.MODULE_ID}, Title: {module.TITLE}, Sequence: {module.SEQUENCE_NUMBER}"
                    });

                    // Check for module content
                    var resources = await connection.QueryAsync(@"
                        SELECT RESOURCE_ID, TITLE, CONTENT_TYPE
                        FROM RESOURCES
                        WHERE MODULE_ID = @ModuleId
                        ORDER BY SEQUENCE_NUMBER",
                        new { ModuleId = module.MODULE_ID });

                    ModuleData.Add(new DebugInfo
                    {
                        Name = $"Module {moduleCount} Resources",
                        Value = resources.Count().ToString()
                    });

                    var quizzes = await connection.QueryAsync(@"
                        SELECT QUIZ_ID, TITLE
                        FROM QUIZZES
                        WHERE MODULE_ID = @ModuleId",
                        new { ModuleId = module.MODULE_ID });

                    ModuleData.Add(new DebugInfo
                    {
                        Name = $"Module {moduleCount} Quizzes",
                        Value = quizzes.Count().ToString()
                    });
                }

                // Assignment data
                var assignments = await connection.QueryAsync(@"
                    SELECT 
                        a.ASSIGNMENT_ID, a.TITLE, a.DUE_DATE, a.MAX_SCORE,
                        m.MODULE_ID, m.TITLE as MODULE_TITLE
                    FROM ASSIGNMENTS a
                    JOIN MODULES m ON a.MODULE_ID = m.MODULE_ID
                    WHERE m.COURSE_ID = @CourseId
                    ORDER BY a.DUE_DATE",
                    new { CourseId = id });

                AssignmentData.Add(new DebugInfo { Name = "Assignment Count", Value = assignments.Count().ToString() });

                int assignmentCount = 0;
                foreach (var assignment in assignments)
                {
                    assignmentCount++;
                    AssignmentData.Add(new DebugInfo
                    {
                        Name = $"Assignment {assignmentCount}",
                        Value = $"ID: {assignment.ASSIGNMENT_ID}, Title: {assignment.TITLE}, Module: {assignment.MODULE_TITLE}"
                    });

                    // Check if there's a submission
                    var submission = await connection.QueryFirstOrDefaultAsync(@"
                        SELECT SUBMISSION_ID, SUBMITTED_ON, GRADE
                        FROM ASSIGNMENT_SUBMISSIONS
                        WHERE ASSIGNMENT_ID = @AssignmentId AND USER_ID = @UserId",
                        new { AssignmentId = assignment.ASSIGNMENT_ID, UserId });

                    AssignmentData.Add(new DebugInfo
                    {
                        Name = $"Assignment {assignmentCount} Submission",
                        Value = submission == null ? "None" : $"Submitted: {submission.SUBMITTED_ON}, Grade: {submission.GRADE}"
                    });
                }

                // User progress data
                var userProgress = await connection.QueryAsync(@"
                    SELECT 
                        MODULE_ID, RESOURCE_ID, QUIZ_ID, 
                        STATUS, LAST_ACCESSED
                    FROM USER_PROGRESS
                    WHERE USER_ID = @UserId
                    AND MODULE_ID IN (SELECT MODULE_ID FROM MODULES WHERE COURSE_ID = @CourseId)",
                    new { UserId, CourseId = id });

                UserProgressData.Add(new DebugInfo { Name = "Progress Records", Value = userProgress.Count().ToString() });

                int progressCount = 0;
                foreach (var progress in userProgress)
                {
                    progressCount++;
                    UserProgressData.Add(new DebugInfo
                    {
                        Name = $"Progress {progressCount}",
                        Value = $"Module: {progress.MODULE_ID}, Resource: {progress.RESOURCE_ID}, Quiz: {progress.QUIZ_ID}, Status: {progress.STATUS}"
                    });
                }

                // Course progress
                var courseProgress = await connection.QueryFirstOrDefaultAsync(@"
                    SELECT PROGRESS, LAST_ACCESSED
                    FROM COURSE_PROGRESS
                    WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId, CourseId = id });

                CourseData.Add(new DebugInfo
                {
                    Name = "Course Progress",
                    Value = courseProgress == null ? "No progress record" : $"{courseProgress.PROGRESS}%, Last accessed: {courseProgress.LAST_ACCESSED}"
                });

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in course debugger");
                ErrorMessage = $"Error connecting to database: {ex.Message}";
                return Page();
            }
        }
    }
}