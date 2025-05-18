using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    [Authorize(Roles = "INSTRUCTOR")]
    public class AssignmentsModel : PageModel
    {
        private readonly string _connectionString;

        public AssignmentsModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        public List<Assignment> Assignments { get; set; } = new List<Assignment>();
        public List<Course> Courses { get; set; } = new List<Course>();
        public SelectList CoursesList { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedCourseId { get; set; }

        public class Assignment
        {
            public int AssignmentId { get; set; }
            public string Title { get; set; }
            public string Instructions { get; set; }
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; }
            public int SubmissionCount { get; set; }
            public int UngradedCount { get; set; }
            public string CourseTitle { get; set; }
        }

        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Verify session exists
            if (!HttpContext.Session.IsAvailable)
            {
                return RedirectToPage("/Login");
            }

            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get instructor's courses
                Courses = (await connection.QueryAsync<Course>(
                    "SELECT COURSE_ID AS CourseId, TITLE AS Title FROM COURSES WHERE CREATED_BY = @InstructorId ORDER BY TITLE",
                    new { InstructorId = userId })).ToList();

                CoursesList = new SelectList(Courses, "CourseId", "Title");

                if (!SelectedCourseId.HasValue && Courses.Any())
                {
                    SelectedCourseId = Courses.First().CourseId;
                }

                if (SelectedCourseId.HasValue)
                {
                    Assignments = (await connection.QueryAsync<Assignment>(
                        @"SELECT 
                            a.ASSIGNMENT_ID AS AssignmentId,
                            a.TITLE AS Title,
                            a.INSTRUCTIONS AS Instructions,
                            a.DUE_DATE AS DueDate,
                            a.MAX_SCORE AS MaxScore,
                            (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = a.ASSIGNMENT_ID) AS SubmissionCount,
                            (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = a.ASSIGNMENT_ID AND GRADE IS NULL) AS UngradedCount,
                            c.TITLE AS CourseTitle
                          FROM ASSIGNMENTS a
                          JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                          WHERE a.COURSE_ID = @CourseId
                          ORDER BY a.DUE_DATE",
                        new { CourseId = SelectedCourseId })).ToList();
                }

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var assignmentInfo = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT a.COURSE_ID AS CourseId
                      FROM ASSIGNMENTS a
                      JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                      WHERE a.ASSIGNMENT_ID = @AssignmentId AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId = id, InstructorId = userId });

                if (assignmentInfo == null)
                {
                    return NotFound();
                }

                await connection.ExecuteAsync(
                    "DELETE FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = id });

                await connection.ExecuteAsync(
                    "DELETE FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = id });

                return RedirectToPage(new { courseId = assignmentInfo.CourseId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error deleting assignment: {ex.Message}");
                return RedirectToPage();
            }
        }
    }
}