using System;

namespace E_Learning_Platform.Models
{
    public class AnalyticsCompletionData
    {
        public int CompletedCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class AnalyticsEnrollmentData
    {
        public string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class AnalyticsCategoryData
    {
        public string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class AnalyticsRecentEnrollment
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime EnrollmentDate { get; set; }
    }

    public class AnalyticsRecentCompletion
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime CompletionDate { get; set; }
        public decimal Score { get; set; }
    }
} 