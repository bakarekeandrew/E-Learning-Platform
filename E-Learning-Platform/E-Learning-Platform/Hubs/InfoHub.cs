using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Hubs
{
    [Authorize]
    public class InfoHub : Hub
    {
        private readonly ILogger<InfoHub> _logger;
        private readonly IConfiguration _configuration;

        public InfoHub(ILogger<InfoHub> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                    _logger.LogInformation("[InfoHub] User {UserId} connected to InfoHub", userId);
                    _logger.LogDebug("[InfoHub] Added connection {ConnectionId} to user group {UserId}", Context.ConnectionId, userId);
                }
                else
                {
                    _logger.LogWarning("[InfoHub] User connected to InfoHub without userId. ConnectionId: {ConnectionId}", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InfoHub] Error in OnConnectedAsync. ConnectionId: {ConnectionId}", Context.ConnectionId);
                throw;
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                    _logger.LogInformation("[InfoHub] User {UserId} disconnected from InfoHub", userId);
                    _logger.LogDebug("[InfoHub] Removed connection {ConnectionId} from user group {UserId}", Context.ConnectionId, userId);
                }

                if (exception != null)
                {
                    _logger.LogError(exception, "[InfoHub] Client disconnected from InfoHub with error. ConnectionId: {ConnectionId}", Context.ConnectionId);
                }
                else
                {
                    _logger.LogInformation("[InfoHub] Client disconnected from InfoHub normally. ConnectionId: {ConnectionId}", Context.ConnectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InfoHub] Error in OnDisconnectedAsync. ConnectionId: {ConnectionId}", Context.ConnectionId);
                throw;
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinUserGroup(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("[InfoHub] Attempted to join user group with empty userId. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    return;
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation("[InfoHub] Added connection {ConnectionId} to user group {UserId}", Context.ConnectionId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InfoHub] Error joining user group {UserId}. ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
                throw;
            }
        }

        public async Task JoinCourseGroup(string courseId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"course_{courseId}");
        }

        public async Task LeaveUserGroup(string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("[InfoHub] Attempted to leave user group with empty userId. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    return;
                }

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                _logger.LogInformation("[InfoHub] Removed connection {ConnectionId} from user group {UserId}", Context.ConnectionId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InfoHub] Error leaving user group {UserId}. ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
                throw;
            }
        }

        public async Task LeaveCourseGroup(string courseId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"course_{courseId}");
        }

        public async Task UpdateUserInfo(UserInfoUpdate update)
        {
            await Clients.Group($"user_{update.UserId}").SendAsync("UserInfoUpdated", update);
        }

        public async Task UpdateCourseInfo(CourseInfoUpdate update)
        {
            await Clients.Group($"course_{update.CourseId}").SendAsync("CourseInfoUpdated", update);
        }

        public async Task UpdateUserProgress(UserProgressUpdate update)
        {
            await Clients.Group($"user_{update.UserId}").SendAsync("UserProgressUpdated", update);
        }

        public async Task UpdateCourseProgress(CourseProgressUpdate update)
        {
            await Clients.Group($"course_{update.CourseId}").SendAsync("CourseProgressUpdated", update);
        }

        public async Task SendUserNotification(string userId, NotificationDto notification)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("[InfoHub] Attempted to send notification with empty userId. ConnectionId: {ConnectionId}", Context.ConnectionId);
                    return;
                }

                _logger.LogDebug("[InfoHub] Sending notification to user {UserId}. Title: {Title}", userId, notification.Title);
                await Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", notification);
                _logger.LogInformation("[InfoHub] Notification sent to user {UserId}: {Title}", userId, notification.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[InfoHub] Error sending notification to user {UserId}. ConnectionId: {ConnectionId}", userId, Context.ConnectionId);
                throw;
            }
        }

        public async Task SendCourseNotification(string courseId, NotificationDto notification)
        {
            try
            {
                if (string.IsNullOrEmpty(courseId))
                {
                    _logger.LogWarning("Attempted to send course notification with empty courseId");
                    return;
                }

                await Clients.Group($"course_{courseId}").SendAsync("ReceiveNotification", notification);
                _logger.LogInformation("Course notification sent to course {CourseId}: {Title}", courseId, notification.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification to course {CourseId}", courseId);
                throw;
            }
        }
    }

    public class UserInfoUpdate
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public DateTime LastActive { get; set; }
        public int EnrolledCourses { get; set; }
        public int CompletedCourses { get; set; }
        public double AverageProgress { get; set; }
        public List<RecentActivity> RecentActivities { get; set; }
    }

    public class RecentActivity
    {
        public string ActivityType { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class UserProgressUpdate
    {
        public string UserId { get; set; }
        public List<CourseProgress> Courses { get; set; }
        public List<AssessmentResult> RecentAssessments { get; set; }
    }

    public class CourseProgress
    {
        public string CourseName { get; set; }
        public double Progress { get; set; }
        public DateTime LastAccessed { get; set; }
        public string Status { get; set; }
    }

    public class CourseInfoUpdate
    {
        public string CourseId { get; set; }
        public string Title { get; set; }
        public string Instructor { get; set; }
        public int EnrolledStudents { get; set; }
        public double CompletionRate { get; set; }
        public double AverageRating { get; set; }
        public int ActiveStudents { get; set; }
        public List<ModuleStatus> Modules { get; set; }
    }

    public class ModuleStatus
    {
        public string ModuleName { get; set; }
        public int TotalStudents { get; set; }
        public int CompletedStudents { get; set; }
        public double AverageScore { get; set; }
    }

    public class CourseProgressUpdate
    {
        public string CourseId { get; set; }
        public List<StudentProgress> StudentProgress { get; set; }
    }

    public class StudentProgress
    {
        public string StudentName { get; set; }
        public double Progress { get; set; }
        public DateTime LastActive { get; set; }
        public string CurrentModule { get; set; }
    }

    public class NotificationDto
    {
        public int NotificationId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }
} 