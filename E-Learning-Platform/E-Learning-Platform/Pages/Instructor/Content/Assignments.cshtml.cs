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
    public class AssignmentsModel : InstructorPageModel
    {
        public List<Assignment> Assignments { get; set; } = new List<Assignment>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public List<Module> Modules { get; set; } = new List<Module>();
        public int? SelectedCourseId { get; set; }
        public int? SelectedModuleId { get; set; }
        public string? CourseName { get; set; }
        public string? ModuleName { get; set; }

        public AssignmentsModel(ILogger<AssignmentsModel> logger, IConfiguration configuration)
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
                            (SELECT COUNT(*) FROM ASSIGNMENTS WHERE MODULE_ID = m.MODULE_ID) AS AssignmentCount
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

                        // Get assignments for selected module with detailed information
                        Assignments = (await connection.QueryAsync<Assignment>(@"
                            WITH AssignmentStats AS (
                                SELECT 
                                    a.ASSIGNMENT_ID,
                                    COUNT(DISTINCT s.SUBMISSION_ID) AS SubmissionCount,
                                    AVG(CAST(ISNULL(s.GRADE, 0) AS FLOAT)) AS AverageScore,
                                    COUNT(CASE WHEN s.SUBMISSION_ID IS NOT NULL AND s.GRADE IS NULL THEN 1 END) AS UngradedCount
                                FROM ASSIGNMENTS a
                                LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID
                                GROUP BY a.ASSIGNMENT_ID
                            )
                            SELECT 
                                a.ASSIGNMENT_ID AS AssignmentId,
                                a.COURSE_ID AS CourseId,
                                a.MODULE_ID AS ModuleId,
                                a.TITLE AS Title,
                                a.INSTRUCTIONS AS Instructions,
                                a.DUE_DATE AS DueDate,
                                a.MAX_SCORE AS MaxScore,
                                ISNULL(ast.SubmissionCount, 0) AS SubmissionCount,
                                ISNULL(ast.AverageScore, 0) AS AverageScore,
                                ISNULL(ast.UngradedCount, 0) AS UngradedCount
                            FROM ASSIGNMENTS a
                            LEFT JOIN AssignmentStats ast ON a.ASSIGNMENT_ID = ast.ASSIGNMENT_ID
                            WHERE a.MODULE_ID = @ModuleId
                            ORDER BY a.DUE_DATE",
                            new { ModuleId = SelectedModuleId })).AsList();
                    }
                }

                return Page();
            }, "Error loading assignments");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify assignment ownership
                var assignmentInfo = await connection.QueryFirstOrDefaultAsync<AssignmentInfo>(@"
                    SELECT 
                        a.MODULE_ID AS ModuleId,
                        a.COURSE_ID AS CourseId
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId = id, InstructorId = GetInstructorId() });

                if (assignmentInfo == null)
                {
                    return NotFound();
                }

                // Delete assignment (cascade delete will handle submissions)
                await connection.ExecuteAsync("DELETE FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId", new { AssignmentId = id });

                TempData["SuccessMessage"] = "Assignment deleted successfully.";
                return RedirectToPage(new { courseId = assignmentInfo.CourseId, moduleId = assignmentInfo.ModuleId });
            }, "Error deleting assignment");
        }

        public class Assignment
        {
            public int AssignmentId { get; set; }
            public int CourseId { get; set; }
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public string? Instructions { get; set; }
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; }
            public int SubmissionCount { get; set; }
            public double? AverageScore { get; set; }
            public int UngradedCount { get; set; }
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
            public int AssignmentCount { get; set; }
        }

        private class AssignmentInfo
        {
            public int ModuleId { get; set; }
            public int CourseId { get; set; }
        }

        // Input model for assignment creation
        public class CreateAssignmentInput
        {
            public required string Title { get; set; }
            public string? Instructions { get; set; }
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; }
            public int ModuleId { get; set; }
        }

        // Input model for assignment editing
        public class EditAssignmentInput
        {
            public int AssignmentId { get; set; }
            public required string Title { get; set; }
            public string? Instructions { get; set; }
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; }
        }

        [BindProperty]
        public CreateAssignmentInput CreateAssignment { get; set; } = null!;

        [BindProperty]
        public EditAssignmentInput EditAssignment { get; set; } = null!;

        public async Task<IActionResult> OnPostCreateAsync()
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
                    new { ModuleId = CreateAssignment.ModuleId, InstructorId = GetInstructorId() });

                if (moduleInfo == null)
                {
                    return NotFound();
                }

                // Create assignment
                var assignmentId = await connection.QuerySingleAsync<int>(@"
                    INSERT INTO ASSIGNMENTS (
                        COURSE_ID,
                        MODULE_ID,
                        TITLE,
                        INSTRUCTIONS,
                        DUE_DATE,
                        MAX_SCORE
                    ) VALUES (
                        @CourseId,
                        @ModuleId,
                        @Title,
                        @Instructions,
                        @DueDate,
                        @MaxScore
                    );
                    SELECT SCOPE_IDENTITY();",
                    new
                    {
                        moduleInfo.CourseId,
                        CreateAssignment.ModuleId,
                        CreateAssignment.Title,
                        CreateAssignment.Instructions,
                        CreateAssignment.DueDate,
                        CreateAssignment.MaxScore
                    });

                TempData["SuccessMessage"] = "Assignment created successfully.";
                return RedirectToPage(new { courseId = moduleInfo.CourseId, moduleId = moduleInfo.ModuleId });
            }, "Error creating assignment");
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            if (!ModelState.IsValid)
            {
                return RedirectToPage();
            }

            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify assignment ownership
                var assignmentInfo = await connection.QueryFirstOrDefaultAsync<AssignmentInfo>(@"
                    SELECT 
                        a.MODULE_ID AS ModuleId,
                        a.COURSE_ID AS CourseId
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId = EditAssignment.AssignmentId, InstructorId = GetInstructorId() });

                if (assignmentInfo == null)
                {
                    return NotFound();
                }

                // Update assignment
                await connection.ExecuteAsync(@"
                    UPDATE ASSIGNMENTS 
                    SET 
                        TITLE = @Title,
                        INSTRUCTIONS = @Instructions,
                        DUE_DATE = @DueDate,
                        MAX_SCORE = @MaxScore
                    WHERE ASSIGNMENT_ID = @AssignmentId",
                    new
                    {
                        EditAssignment.AssignmentId,
                        EditAssignment.Title,
                        EditAssignment.Instructions,
                        EditAssignment.DueDate,
                        EditAssignment.MaxScore
                    });

                TempData["SuccessMessage"] = "Assignment updated successfully.";
                return RedirectToPage(new { courseId = assignmentInfo.CourseId, moduleId = assignmentInfo.ModuleId });
            }, "Error updating assignment");
        }

        private class ModuleInfo
        {
            public int ModuleId { get; set; }
            public int CourseId { get; set; }
        }
    }
}