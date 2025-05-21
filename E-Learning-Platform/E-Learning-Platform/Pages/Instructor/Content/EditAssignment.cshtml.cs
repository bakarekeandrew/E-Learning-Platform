using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace E_Learning_Platform.Pages.Instructor.Content
{
    [Authorize(Roles = "INSTRUCTOR")]
    public class EditAssignmentModel : PageModel
    {
        private readonly string _connectionString;

        public EditAssignmentModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        [BindProperty]
        public int AssignmentId { get; set; }

        [BindProperty]
        public required string Title { get; set; }

        [BindProperty]
        public required string Instructions { get; set; }

        [BindProperty]
        public DateTime? DueDate { get; set; }

        [BindProperty]
        public int MaxScore { get; set; }

        [BindProperty(SupportsGet = true)]
        public int CourseId { get; set; }

        private class AssignmentInfo
        {
            public int AssignmentId { get; set; }
            public required string Title { get; set; }
            public required string Instructions { get; set; }
            public DateTime? DueDate { get; set; }
            public int MaxScore { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int assignmentId, int courseId)
        {
            CourseId = courseId;
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);

                var assignment = await connection.QueryFirstOrDefaultAsync<EditAssignmentModel>(
                    @"SELECT a.ASSIGNMENT_ID AS AssignmentId, 
                             a.TITLE AS Title, 
                             a.INSTRUCTIONS AS Instructions, 
                             a.DUE_DATE AS DueDate, 
                             a.MAX_SCORE AS MaxScore
                      FROM ASSIGNMENTS a
                      JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                      WHERE a.ASSIGNMENT_ID = @AssignmentId 
                      AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId = assignmentId, InstructorId = userId });

                if (assignment == null) return NotFound();

                AssignmentId = assignment.AssignmentId;
                Title = assignment.Title ?? string.Empty;
                Instructions = assignment.Instructions ?? string.Empty;
                DueDate = assignment.DueDate;
                MaxScore = assignment.MaxScore;

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error loading assignment: {ex.Message}");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }

            if (!ModelState.IsValid) return Page();

            try
            {
                using var connection = new SqlConnection(_connectionString);

                var isValid = await connection.ExecuteScalarAsync<bool>(
                    @"SELECT 1 
                      FROM ASSIGNMENTS a
                      JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                      WHERE a.ASSIGNMENT_ID = @AssignmentId 
                      AND c.CREATED_BY = @InstructorId",
                    new { AssignmentId, InstructorId = userId });

                if (!isValid) return Forbid();

                await connection.ExecuteAsync(
                    @"UPDATE ASSIGNMENTS 
                      SET TITLE = @Title, 
                          INSTRUCTIONS = @Instructions, 
                          DUE_DATE = @DueDate, 
                          MAX_SCORE = @MaxScore
                      WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId, Title, Instructions, DueDate, MaxScore });

                return RedirectToPage("/Instructor/Content/Assignments",
                    new { SelectedCourseId = CourseId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error updating assignment: {ex.Message}");
                return Page();
            }
        }
    }
}