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
        public required string MonthName { get; set; }
        public int EnrollmentCount { get; set; }
    }

    public class AnalyticsCategoryData
    {
        public required string CategoryName { get; set; }
        public int CourseCount { get; set; }
    }

    public class AnalyticsRecentEnrollment
    {
        public required string CourseName { get; set; }
        public required string StudentName { get; set; }
        public DateTime EnrollmentDate { get; set; }
    }

    public class AnalyticsRecentCompletion
    {
        public required string CourseName { get; set; }
        public required string StudentName { get; set; }
        public DateTime CompletionDate { get; set; }
        public decimal Score { get; set; }
    }

    public class AnalyticsSystemStatus
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required string DatabaseStatus { get; set; }
        public required string SessionStatus { get; set; }
        public required string AuthStatus { get; set; }
        public required string UserDetails { get; set; }
        public required string ClaimsDetails { get; set; }

        public AnalyticsSystemStatus()
        {
            Title = "System Status";
            Description = "Current system status and health metrics";
            DatabaseStatus = "Unknown";
            SessionStatus = "Unknown";
            AuthStatus = "Unknown";
            UserDetails = "Not available";
            ClaimsDetails = "Not available";
        }
    }
} 