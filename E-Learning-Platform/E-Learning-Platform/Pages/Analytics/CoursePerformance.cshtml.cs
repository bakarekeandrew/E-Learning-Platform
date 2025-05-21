using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;

namespace E_Learning_Platform.Pages.Analytics
{
    public class CoursePerformanceModel : PageModel
    {
        private readonly string _connectionString;

        public CoursePerformanceModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        // Course Performance Metrics
        public double AverageCompletionRate { get; set; }
        public double AverageStudentScore { get; set; }
        public double StudentSatisfaction { get; set; }
        public int TotalEnrollments { get; set; }
        public int CompletedCourses { get; set; }
        public List<CoursePerformanceData> TopPerformingCourses { get; set; }
        public List<CoursePerformanceData> NeedsImprovementCourses { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                await LoadCoursePerformanceData();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : ""));
            }
        }

        private async Task LoadCoursePerformanceData()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Average Completion Rate: percent of enrollments with COMPLETION_DATE not null
            var totalEnrollments = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM COURSE_ENROLLMENTS");
            var completedEnrollments = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE COMPLETION_DATE IS NOT NULL");
            AverageCompletionRate = totalEnrollments > 0 ? (double)completedEnrollments / totalEnrollments * 100 : 0;

            // Average Student Score: average GRADE from ASSIGNMENT_SUBMISSIONS
            var avgScore = await connection.ExecuteScalarAsync<decimal?>("SELECT AVG(CAST(GRADE AS FLOAT)) FROM ASSIGNMENT_SUBMISSIONS WHERE GRADE IS NOT NULL");
            AverageStudentScore = avgScore.HasValue ? (double)avgScore.Value : 0;

            // Student Satisfaction: average RATING from ASSIGNMENT_SUBMISSIONS (if exists), else set to 0
            var hasRating = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ASSIGNMENT_SUBMISSIONS' AND COLUMN_NAME = 'RATING'");
            if (hasRating > 0)
            {
                var avgSatisfaction = await connection.ExecuteScalarAsync<decimal?>("SELECT AVG(CAST(RATING AS FLOAT)) FROM ASSIGNMENT_SUBMISSIONS WHERE RATING IS NOT NULL");
                StudentSatisfaction = avgSatisfaction.HasValue ? (double)avgSatisfaction.Value : 0;
            }
            else
            {
                StudentSatisfaction = 0;
            }

            // Total Enrollments
            TotalEnrollments = totalEnrollments;

            // Completed Courses
            CompletedCourses = completedEnrollments;

            // Top Performing Courses: by completion rate and average score
            TopPerformingCourses = (await connection.QueryAsync<CoursePerformanceData>(@"
                SELECT TOP 5 
                    c.TITLE as Title,
                    COUNT(ce.ENROLLMENT_ID) as EnrollmentCount,
                    CAST(COUNT(ce.COMPLETION_DATE) * 100.0 / NULLIF(COUNT(ce.ENROLLMENT_ID),0) AS FLOAT) as CompletionRate,
                    ISNULL(AVG(CAST(s.GRADE AS FLOAT)), 0) as AverageScore
                FROM COURSES c
                LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON ce.USER_ID = s.USER_ID AND ce.COURSE_ID = s.ASSIGNMENT_ID
                GROUP BY c.TITLE
                ORDER BY CompletionRate DESC, AverageScore DESC")).AsList();

            // Courses Needing Improvement: lowest completion rate and average score
            NeedsImprovementCourses = (await connection.QueryAsync<CoursePerformanceData>(@"
                SELECT TOP 5 
                    c.TITLE as Title,
                    COUNT(ce.ENROLLMENT_ID) as EnrollmentCount,
                    CAST(COUNT(ce.COMPLETION_DATE) * 100.0 / NULLIF(COUNT(ce.ENROLLMENT_ID),0) AS FLOAT) as CompletionRate,
                    ISNULL(AVG(CAST(s.GRADE AS FLOAT)), 0) as AverageScore
                FROM COURSES c
                LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON ce.USER_ID = s.USER_ID AND ce.COURSE_ID = s.ASSIGNMENT_ID
                GROUP BY c.TITLE
                ORDER BY CompletionRate ASC, AverageScore ASC")).AsList();
        }
    }

    public class CoursePerformanceData
    {
        public string Title { get; set; }
        public int EnrollmentCount { get; set; }
        public double CompletionRate { get; set; }
        public double AverageScore { get; set; }
    }
} 