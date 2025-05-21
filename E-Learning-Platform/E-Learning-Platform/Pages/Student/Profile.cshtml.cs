using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Http;
using System;

namespace E_Learning_Platform.Pages.Student
{
    public class ProfileModel : PageModel
    {
        private readonly string _connectionString;

        public ProfileModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        public string StudentId { get; set; }
        public string StudentName { get; set; }
        public string StudentEmail { get; set; }
        public string StudentInitials { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime EnrollmentDate { get; set; }
        public int CoursesCount { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }

            var userId = BitConverter.ToInt32(userIdBytes, 0);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var student = await connection.QueryFirstOrDefaultAsync<StudentProfile>(@"
                    SELECT 
                        u.USER_ID AS StudentId,
                        u.FULL_NAME AS StudentName,
                        u.EMAIL AS StudentEmail,
                        u.FIRST_NAME AS FirstName,
                        u.LAST_NAME AS LastName,
                        MIN(ce.ENROLLMENT_DATE) AS EnrollmentDate,
                        COUNT(DISTINCT ce.COURSE_ID) AS CoursesCount
                    FROM USERS u
                    LEFT JOIN COURSE_ENROLLMENTS ce ON u.USER_ID = ce.USER_ID
                    WHERE u.USER_ID = @UserId
                    GROUP BY u.USER_ID, u.FULL_NAME, u.EMAIL, u.FIRST_NAME, u.LAST_NAME",
                    new { UserId = userId });

                if (student != null)
                {
                    StudentId = student.StudentId.ToString();
                    StudentName = student.StudentName;
                    StudentEmail = student.StudentEmail;
                    FirstName = student.FirstName;
                    LastName = student.LastName;
                    EnrollmentDate = student.EnrollmentDate;
                    CoursesCount = student.CoursesCount;

                    // Generate initials
                    var initials = "";
                    if (!string.IsNullOrEmpty(FirstName)) initials += FirstName[0];
                    if (!string.IsNullOrEmpty(LastName)) initials += LastName[0];
                    StudentInitials = string.IsNullOrEmpty(initials) ? "U" : initials;
                }

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while loading profile");
                return Page();
            }
        }

        public class StudentProfile
        {
            public int StudentId { get; set; }
            public string StudentName { get; set; }
            public string StudentEmail { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public DateTime EnrollmentDate { get; set; }
            public int CoursesCount { get; set; }
        }
    }
}