using System;
using System.Collections.Generic;
using E_Learning_Platform.Hubs;

namespace E_Learning_Platform.Services
{
    public class NotificationDto
    {
        public int NotificationId { get; set; }
        public int UserId { get; set; }
        public required string Title { get; set; }
        public required string Message { get; set; }
        public required string Type { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationEvent : EventArgs
    {
        public required int UserId { get; set; }
        public required string Title { get; set; }
        public required string Message { get; set; }
        public required string Type { get; set; }
    }

    public interface INotificationEventService
    {
        void Subscribe(EventHandler<NotificationEvent> handler);
        void Unsubscribe(EventHandler<NotificationEvent> handler);
        void RaiseEvent(NotificationEvent notification);
    }

    public class ChartData
    {
        public required List<string> Labels { get; set; }
        public required List<int> ActivityData { get; set; }
        public required List<int> CompletionData { get; set; }
        public required List<double> ErrorRateData { get; set; }
        public required List<string> TimeSlots { get; set; }
        public required List<int> ActiveStudents { get; set; }
        public required List<int> ResourceAccess { get; set; }
        public required List<TopEngagedStudent> TopEngaged { get; set; }
    }

    public class TopEngagedStudent
    {
        public required string StudentName { get; set; }
        public required int ResourceViews { get; set; }
        public required double AverageTimeSpent { get; set; }
        public required List<string> PopularTimeSlots { get; set; }
        public required Dictionary<string, double> ResourceEffectiveness { get; set; }
        public required Dictionary<string, double> ModuleProgress { get; set; }
        public required List<AssessmentResult> RecentAssessments { get; set; }
        public required Dictionary<string, double> SkillProgress { get; set; }
    }

    public class CourseAnalytics
    {
        public required string Title { get; set; }
        public required string InstructorName { get; set; }
        public required string ThumbnailUrl { get; set; }
        public required string Role { get; set; }
        public required string Month { get; set; }
        public required string CategoryName { get; set; }
        public int EnrollmentCount { get; set; }
        public double CompletionRate { get; set; }
        public double AverageScore { get; set; }
        public int ActiveUsers { get; set; }
    }

    public class ErrorLog
    {
        public required string ErrorType { get; set; }
        public required string Path { get; set; }
        public required string Message { get; set; }
        public required string TimeLabel { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AnalyticsReport
    {
        public required List<string> SelectedMetrics { get; set; }
        public required Dictionary<string, object> ReportData { get; set; }
    }
} 