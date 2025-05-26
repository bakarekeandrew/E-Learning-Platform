using System;
using System.Collections.Generic;

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

    public class DashboardData
    {
        public DashboardData()
        {
            Labels = new List<string>();
            ActivityData = new List<int>();
            CompletionData = new List<double>();
            ErrorRateData = new List<double>();
            TimeSlots = new List<string>();
            ActiveStudents = new List<string>();
            ResourceAccess = new List<int>();
            TopEngaged = new List<string>();
            ResourceViews = new List<int>();
            AverageTimeSpent = new List<double>();
            PopularTimeSlots = new List<string>();
            ResourceEffectiveness = new List<double>();
            ModuleProgress = new List<double>();
            RecentAssessments = new List<AssessmentData>();
            SkillProgress = new List<SkillData>();
            StudentName = string.Empty;
            CourseName = string.Empty;
            Notes = string.Empty;
            MonthName = string.Empty;
            CategoryName = string.Empty;
            Title = string.Empty;
            InstructorName = string.Empty;
            ThumbnailUrl = string.Empty;
            Role = string.Empty;
            Month = string.Empty;
            ErrorType = string.Empty;
        }

        public List<string> Labels { get; set; }
        public List<int> ActivityData { get; set; }
        public List<double> CompletionData { get; set; }
        public List<double> ErrorRateData { get; set; }
        public List<string> TimeSlots { get; set; }
        public List<string> ActiveStudents { get; set; }
        public List<int> ResourceAccess { get; set; }
        public List<string> TopEngaged { get; set; }
        public List<int> ResourceViews { get; set; }
        public List<double> AverageTimeSpent { get; set; }
        public List<string> PopularTimeSlots { get; set; }
        public List<double> ResourceEffectiveness { get; set; }
        public List<double> ModuleProgress { get; set; }
        public List<AssessmentData> RecentAssessments { get; set; }
        public List<SkillData> SkillProgress { get; set; }
        public string StudentName { get; set; }
        public string CourseName { get; set; }
        public string Notes { get; set; }
        public string MonthName { get; set; }
        public string CategoryName { get; set; }
        public string Title { get; set; }
        public string InstructorName { get; set; }
        public string ThumbnailUrl { get; set; }
        public string Role { get; set; }
        public string Month { get; set; }
        public string ErrorType { get; set; }
    }

    public class AssessmentData
    {
        public AssessmentData()
        {
            AssessmentName = string.Empty;
            StudentName = string.Empty;
            CourseName = string.Empty;
        }

        public string AssessmentName { get; set; }
        public string StudentName { get; set; }
        public string CourseName { get; set; }
        public double Score { get; set; }
        public DateTime CompletedDate { get; set; }
    }

    public class SkillData
    {
        public SkillData()
        {
            SkillName = string.Empty;
            CategoryName = string.Empty;
        }

        public string SkillName { get; set; }
        public string CategoryName { get; set; }
        public double Progress { get; set; }
        public DateTime LastUpdated { get; set; }
    }
} 