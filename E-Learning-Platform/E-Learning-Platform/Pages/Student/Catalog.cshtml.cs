using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;

namespace E_Learning_Platform.Pages.Student
{
    public class CatalogModel : PageModel
    {
        private readonly string _connectionString;

        public CatalogModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        public List<AvailableCourse> AvailableCourses { get; set; } = new List<AvailableCourse>();
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                {
                    return RedirectToPage("/Login");
                }

                var userId = BitConverter.ToInt32(userIdBytes, 0);

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    // First verify the user exists
                    var userExists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId });

                    if (!userExists)
                    {
                        ErrorMessage = "User not found";
                        return Page();
                    }

                    // Get all available courses that the student is NOT enrolled in
                    string query = @"
                        SELECT 
                            c.COURSE_ID AS CourseId,
                            c.TITLE AS Title,
                            c.DESCRIPTION AS Description,
                            c.THUMBNAIL_URL AS ThumbnailUrl,
                            u.FULL_NAME AS Instructor,
                            cc.NAME AS Category,
                            'Intermediate' AS Difficulty,
                            10 AS Duration,
                            0 AS Price,
                            (SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = c.COURSE_ID) AS ModuleCount
                        FROM COURSES c
                        JOIN USERS u ON c.CREATED_BY = u.USER_ID
                        LEFT JOIN COURSE_CATEGORIES cc ON c.CATEGORY_ID = cc.CATEGORY_ID
                        WHERE c.IS_ACTIVE = 1
                        AND NOT EXISTS (
                            SELECT 1 FROM COURSE_ENROLLMENTS 
                            WHERE USER_ID = @UserId AND COURSE_ID = c.COURSE_ID
                        )
                        ORDER BY c.CREATION_DATE DESC";

                    AvailableCourses = (await connection.QueryAsync<AvailableCourse>(query, new { UserId = userId })).ToList();
                }

                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading courses: {ex.Message}";
                return Page();
            }
        }

        public class AvailableCourse
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required string ThumbnailUrl { get; set; }
            public required string Instructor { get; set; }
            public required string Category { get; set; }
            public required string Difficulty { get; set; }
            public int Duration { get; set; }
            public decimal Price { get; set; }
            public int ModuleCount { get; set; }
        }

        public class CourseViewModel
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required string ThumbnailUrl { get; set; }
            public required string Instructor { get; set; }
            public required string Category { get; set; }
            public required string Difficulty { get; set; }
            public decimal Rating { get; set; }
            public int EnrollmentCount { get; set; }
            public decimal Price { get; set; }
            public bool IsEnrolled { get; set; }
            public bool IsCompleted { get; set; }
            public int ProgressPercentage { get; set; }
        }
    }
}