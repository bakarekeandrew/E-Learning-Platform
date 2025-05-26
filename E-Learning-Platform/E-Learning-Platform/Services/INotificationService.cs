using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface INotificationService : IDisposable
    {
        Task<bool> CreateNotificationAsync(int userId, string title, string message, string type);
        Task<bool> CreateNotificationForRoleAsync(int roleId, string title, string message, string type);
        Task<List<NotificationDto>> GetUserNotificationsAsync(int userId, bool unreadOnly = false);
        Task<bool> MarkAsReadAsync(int notificationId);
        Task<bool> MarkAllAsReadAsync(int userId);
        Task<bool> DeleteNotificationAsync(int notificationId);
        Task<int> GetUnreadCountAsync(int userId);
    }
} 