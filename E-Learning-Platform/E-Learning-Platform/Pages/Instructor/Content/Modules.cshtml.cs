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
    public class ModulesModel : InstructorPageModel
    {
        public List<Module> Modules { get; set; } = new List<Module>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public int? SelectedCourseId { get; set; }
        public string CourseName { get; set; }

        public ModulesModel(ILogger<ModulesModel> logger, IConfiguration configuration) 
            : base(logger, configuration)
        {
        }

        public async Task<IActionResult> OnGetAsync(int? courseId = null)
        {
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

                    // Get modules for selected course with additional information
                    Modules = (await connection.QueryAsync<Module>(@"
                        SELECT 
                            m.MODULE_ID AS ModuleId,
                            m.TITLE AS Title,
                            m.DESCRIPTION AS Description,
                            m.SEQUENCE_NUMBER AS SequenceNumber,
                            m.IS_FREE AS IsFree,
                            m.DURATION_MINUTES AS DurationMinutes,
                            (SELECT COUNT(*) FROM QUIZZES WHERE MODULE_ID = m.MODULE_ID) AS QuizCount,
                            (SELECT COUNT(*) FROM ASSIGNMENTS WHERE MODULE_ID = m.MODULE_ID) AS AssignmentCount
                        FROM MODULES m
                        WHERE m.COURSE_ID = @CourseId
                        ORDER BY m.SEQUENCE_NUMBER",
                        new { CourseId = SelectedCourseId })).AsList();
                }

                return Page();
            }, "Error loading modules");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify module ownership
                var moduleInfo = await connection.QueryFirstOrDefaultAsync<ModuleInfo>(@"
                    SELECT m.COURSE_ID AS CourseId
                    FROM MODULES m
                    JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                    WHERE m.MODULE_ID = @ModuleId AND c.CREATED_BY = @InstructorId",
                    new { ModuleId = id, InstructorId = GetInstructorId() });

                if (moduleInfo == null)
                {
                    return NotFound();
                }

                // Delete related records first
                await connection.ExecuteAsync("DELETE FROM QUIZ_OPTIONS WHERE QUESTION_ID IN (SELECT QUESTION_ID FROM QUIZ_QUESTIONS WHERE QUIZ_ID IN (SELECT QUIZ_ID FROM QUIZZES WHERE MODULE_ID = @ModuleId))", new { ModuleId = id });
                await connection.ExecuteAsync("DELETE FROM QUIZ_QUESTIONS WHERE QUIZ_ID IN (SELECT QUIZ_ID FROM QUIZZES WHERE MODULE_ID = @ModuleId)", new { ModuleId = id });
                await connection.ExecuteAsync("DELETE FROM QUIZZES WHERE MODULE_ID = @ModuleId", new { ModuleId = id });
                await connection.ExecuteAsync("DELETE FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID IN (SELECT ASSIGNMENT_ID FROM ASSIGNMENTS WHERE MODULE_ID = @ModuleId)", new { ModuleId = id });
                await connection.ExecuteAsync("DELETE FROM ASSIGNMENTS WHERE MODULE_ID = @ModuleId", new { ModuleId = id });
                await connection.ExecuteAsync("DELETE FROM MODULES WHERE MODULE_ID = @ModuleId", new { ModuleId = id });

                return RedirectToPage(new { courseId = moduleInfo.CourseId });
            }, "Error deleting module");
        }

        public class Module
        {
            public int ModuleId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int SequenceNumber { get; set; }
            public bool IsFree { get; set; }
            public int? DurationMinutes { get; set; }
            public int QuizCount { get; set; }
            public int AssignmentCount { get; set; }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public int ModuleCount { get; set; }
        }

        private class ModuleInfo
        {
            public int CourseId { get; set; }
        }
    }
}