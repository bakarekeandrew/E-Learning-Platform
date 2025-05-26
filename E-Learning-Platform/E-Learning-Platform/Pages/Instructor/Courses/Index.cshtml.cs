using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using E_Learning_Platform.Pages.Instructor.Infrastructure;

namespace E_Learning_Platform.Pages.Instructor.Courses
{
    public class IndexModel : InstructorPageModel
    {
        public List<Course> Courses { get; set; } = new List<Course>();

        public IndexModel(ILogger<IndexModel> logger, IConfiguration configuration) 
            : base(logger, configuration)
        {
        }

        public async Task<IActionResult> OnGetAsync()
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get instructor's courses with detailed information
                Courses = (await connection.QueryAsync<Course>(@"
                    WITH CourseStats AS (
                        SELECT 
                            c.COURSE_ID,
                            COUNT(DISTINCT ce.ENROLLMENT_ID) AS StudentCount,
                            COALESCE(AVG(CAST(r.RATING AS DECIMAL(3,2))), 0) AS Rating,
                            (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = c.COURSE_ID) AS ModuleCount
                        FROM COURSES c
                        LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                        LEFT JOIN REVIEWS r ON c.COURSE_ID = r.COURSE_ID
                        WHERE c.CREATED_BY = @InstructorId
                        GROUP BY c.COURSE_ID
                    )
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.DESCRIPTION AS Description,
                        c.IS_ACTIVE AS IsActive,
                        c.CREATION_DATE AS CreatedDate,
                        cs.StudentCount,
                        cs.Rating,
                        cs.ModuleCount
                    FROM COURSES c
                    JOIN CourseStats cs ON c.COURSE_ID = cs.COURSE_ID
                    WHERE c.CREATED_BY = @InstructorId
                    ORDER BY c.CREATION_DATE DESC",
                    new { InstructorId = GetInstructorId() })).AsList();

                return Page();
            }, "Error loading instructor courses");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            return await ExecuteDbOperationAsync(async () =>
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // First verify ownership
                var courseExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM COURSES WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId = id, InstructorId = GetInstructorId() });

                if (!courseExists)
                {
                    return NotFound();
                }

                // Delete related records first
                await connection.ExecuteAsync("DELETE FROM COURSE_ENROLLMENTS WHERE COURSE_ID = @CourseId", new { CourseId = id });
                await connection.ExecuteAsync("DELETE FROM REVIEWS WHERE COURSE_ID = @CourseId", new { CourseId = id });
                await connection.ExecuteAsync("DELETE FROM MODULES WHERE COURSE_ID = @CourseId", new { CourseId = id });
                await connection.ExecuteAsync("DELETE FROM COURSES WHERE COURSE_ID = @CourseId", new { CourseId = id });

                return RedirectToPage();
            }, "Error deleting course");
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int StudentCount { get; set; }
            public decimal Rating { get; set; }
            public bool IsActive { get; set; }
            public DateTime CreatedDate { get; set; }
            public int ModuleCount { get; set; }
        }
    }
}