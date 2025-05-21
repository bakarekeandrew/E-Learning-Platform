using System;

namespace E_Learning_Platform.Models
{
    public class CompletionData
    {
        public int CompletedCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class EnrollmentData
    {
        public string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class CategoryData
    {
        public string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class RecentEnrollment
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime EnrollmentDate { get; set; }
    }

    public class RecentCompletion
    {
        public string CourseName { get; set; }
        public string StudentName { get; set; }
        public DateTime CompletionDate { get; set; }
        public decimal Score { get; set; }
    }
} 