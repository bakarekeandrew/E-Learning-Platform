namespace E_Learning_Platform.Models
{
    public class UserEngagementSummary
    {
        public int DailyActiveUsers { get; set; }
        public double AverageSessionDuration { get; set; }
        public double RetentionRate { get; set; }
    }

    public class CoursePerformanceSummary
    {
        public double AverageCompletionRate { get; set; }
        public double AverageStudentScore { get; set; }
        public double StudentSatisfaction { get; set; }
    }

    public class SystemHealthSummary
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public int ActiveConnections { get; set; }
    }

    public class DashboardMetrics
    {
        public int TotalCourses { get; set; }
        public int ActiveStudents { get; set; }
        public int ActiveInstructors { get; set; }
        public int NewRegistrationsToday { get; set; }
        public List<string> MonthLabels { get; set; } = new List<string>();
        public List<int> EnrollmentTrend { get; set; } = new List<int>();
        public List<string> CategoryLabels { get; set; } = new List<string>();
        public List<int> CategoryDistribution { get; set; } = new List<int>();
    }

    public class RecentActivity
    {
        public string UserName { get; set; }
        public string Activity { get; set; }
        public string CourseName { get; set; }
        public DateTime Date { get; set; }
        public string Status { get; set; }
    }
} 