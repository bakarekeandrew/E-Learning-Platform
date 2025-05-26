using Microsoft.AspNetCore.SignalR;

namespace E_Learning_Platform.Hubs
{
    public class DashboardHub : Hub
    {
        public async Task UpdateActiveUsers(int count)
        {
            await Clients.All.SendAsync("UpdateActiveUsers", count);
        }

        public async Task UpdateCourseProgress(string courseId, decimal progress)
        {
            await Clients.All.SendAsync("CourseProgressUpdated", courseId, progress);
        }

        public async Task UpdateQuizActivity(string quizId, int participantCount)
        {
            await Clients.All.SendAsync("QuizActivityUpdated", quizId, participantCount);
        }

        public async Task SendNotification(string message, string type)
        {
            await Clients.All.SendAsync("ReceiveNotification", message, type);
        }

        // New methods for analytics
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task UpdateSystemMetrics(SystemMetricsUpdate metrics)
        {
            await Clients.Group("analytics").SendAsync("UpdateSystemMetrics", metrics);
        }

        public async Task UpdateUserActivity(UserActivityUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateUserActivity", data);
        }

        public async Task UpdateCourseAnalytics(CourseAnalyticsUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateCourseProgress", data);
        }

        public async Task UpdateErrorMetrics(ErrorMetricsUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateErrorRate", data);
        }

        public async Task UpdateStudentEngagement(StudentEngagementUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateStudentEngagement", data);
        }

        public async Task UpdateLearningProgress(LearningProgressUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateLearningProgress", data);
        }

        public async Task UpdateResourceUtilization(ResourceUtilizationUpdate data)
        {
            await Clients.Group("analytics").SendAsync("UpdateResourceUtilization", data);
        }
    }

    // Data transfer objects for analytics updates
    public class SystemMetricsUpdate
    {
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public int DatabaseConnections { get; set; }
        public int ResponseTime { get; set; }
    }

    public class UserActivityUpdate
    {
        public List<string> Labels { get; set; }
        public List<int> ActivityData { get; set; }
    }

    public class CourseAnalyticsUpdate
    {
        public List<string> Labels { get; set; }
        public List<double> CompletionData { get; set; }
    }

    public class ErrorMetricsUpdate
    {
        public List<string> Labels { get; set; }
        public List<double> ErrorRateData { get; set; }
    }

    public class StudentEngagementUpdate
    {
        public List<string> TimeSlots { get; set; }
        public List<int> ActiveStudents { get; set; }
        public Dictionary<string, int> ResourceAccess { get; set; }
        public List<TopEngagementMetric> TopEngaged { get; set; }
    }

    public class TopEngagementMetric
    {
        public string StudentName { get; set; }
        public int MinutesEngaged { get; set; }
        public int ResourcesAccessed { get; set; }
        public double CompletionRate { get; set; }
    }

    public class ResourceUtilizationUpdate
    {
        public Dictionary<string, int> ResourceViews { get; set; }
        public Dictionary<string, double> AverageTimeSpent { get; set; }
        public List<string> PopularTimeSlots { get; set; }
        public Dictionary<string, double> ResourceEffectiveness { get; set; }
    }

    public class LearningProgressUpdate
    {
        public Dictionary<string, int> ModuleProgress { get; set; }
        public List<AssessmentMetric> RecentAssessments { get; set; }
        public Dictionary<string, double> SkillProgress { get; set; }
    }

    public class AssessmentMetric
    {
        public string AssessmentName { get; set; }
        public double AverageScore { get; set; }
        public int Participants { get; set; }
        public DateTime CompletionDate { get; set; }
    }
}