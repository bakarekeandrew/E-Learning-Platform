using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using E_Learning_Platform.Hubs;

namespace E_Learning_Platform.Services
{
    public class NotificationService : INotificationService
    {
        private readonly string _connectionString;
        private readonly IHubContext<InfoHub> _hubContext;
        private readonly ILogger<NotificationService> _logger;
        private readonly INotificationEventService _eventService;
        private bool _isSubscribed = false;

        public NotificationService(
            IConfiguration configuration,
            IHubContext<InfoHub> hubContext,
            ILogger<NotificationService> logger,
            INotificationEventService eventService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new ArgumentNullException(nameof(configuration));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));

            // Subscribe to notification events
            SubscribeToEvents();
            _logger.LogInformation("[NotificationService] Service initialized");
        }

        private void SubscribeToEvents()
        {
            if (!_isSubscribed)
            {
                _logger.LogDebug("[NotificationService] Subscribing to notification events");
                _eventService.Subscribe(HandleNotificationEvent);
                _isSubscribed = true;
                _logger.LogInformation("[NotificationService] Successfully subscribed to notification events");
            }
        }

        private void HandleNotificationEvent(object? sender, NotificationEvent e)
        {
            _logger.LogInformation("[NotificationService] Received notification event. Title: {Title}, Type: {Type}, UserId: {UserId}", 
                e.Title, e.Type, e.UserId);

            try
            {
                // Create a task to handle the notification asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("[NotificationService] Starting async notification handling for user {UserId}", e.UserId);
                        var result = await CreateNotificationAsync(e.UserId, e.Title, e.Message, e.Type);
                        
                        if (!result)
                        {
                            _logger.LogWarning("[NotificationService] Failed to create notification from event. UserId: {UserId}, Title: {Title}", 
                                e.UserId, e.Title);
                        }
                        else
                        {
                            _logger.LogInformation("[NotificationService] Successfully processed notification event. UserId: {UserId}, Title: {Title}", 
                                e.UserId, e.Title);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[NotificationService] Error in async notification handling for user {UserId}", e.UserId);
                    }
                }).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "[NotificationService] Task faulted while handling notification for user {UserId}", e.UserId);
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationService] Error initiating notification handling for user {UserId}", e.UserId);
            }
        }

        public void Dispose()
        {
            try
            {
                if (_isSubscribed)
                {
                    _logger.LogDebug("[NotificationService] Unsubscribing from notification events");
                    _eventService.Unsubscribe(HandleNotificationEvent);
                    _isSubscribed = false;
                    _logger.LogInformation("[NotificationService] Successfully unsubscribed from notification events");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationService] Error during disposal");
            }
            GC.SuppressFinalize(this);
        }

        public async Task<bool> CreateNotificationAsync(int userId, string title, string message, string type)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(type);

            _logger.LogInformation("[NotificationService] Starting notification creation. UserId: {UserId}, Title: {Title}, Type: {Type}", 
                userId, title, type);
            _logger.LogDebug("[NotificationService] Notification message: {Message}", message);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogDebug("[NotificationService] Opening database connection");
                await connection.OpenAsync();
                _logger.LogDebug("[NotificationService] Database connection opened successfully");

                using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // First verify if the user exists
                    _logger.LogDebug("[NotificationService] Verifying user existence. UserId: {UserId}", userId);
                    var userExists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT 1 FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId },
                        transaction: transaction);

                    if (!userExists)
                    {
                        _logger.LogWarning("[NotificationService] User not found. UserId: {UserId}", userId);
                        return false;
                    }

                    _logger.LogDebug("[NotificationService] User verified. Proceeding with notification creation");

                    var sql = @"
                        INSERT INTO NOTIFICATIONS (
                            USER_ID,
                            TITLE,
                            MESSAGE,
                            TYPE,
                            IS_READ,
                            CREATED_AT
                        ) VALUES (
                            @UserId,
                            @Title,
                            @Message,
                            @Type,
                            0,
                            GETDATE()
                        );
                        SELECT CAST(SCOPE_IDENTITY() as int)";

                    _logger.LogDebug("[NotificationService] Executing notification insert query");
                    var notificationId = await connection.ExecuteScalarAsync<int>(
                        sql,
                        new { UserId = userId, Title = title, Message = message, Type = type },
                        transaction: transaction
                    );

                    _logger.LogDebug("[NotificationService] Insert query completed. NotificationId: {NotificationId}", notificationId);

                    if (notificationId > 0)
                    {
                        var notification = new NotificationDto
                        {
                            NotificationId = notificationId,
                            UserId = userId,
                            Title = title,
                            Message = message,
                            Type = type,
                            IsRead = false,
                            CreatedAt = DateTime.UtcNow
                        };

                        _logger.LogDebug("[NotificationService] Committing transaction");
                        await transaction.CommitAsync();
                        _logger.LogDebug("[NotificationService] Transaction committed successfully");

                        // Send real-time notification via SignalR
                        _logger.LogDebug("[NotificationService] Attempting to send real-time notification via SignalR. UserId: {UserId}", userId);
                        try 
                        {
                            await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", notification);
                            _logger.LogDebug("[NotificationService] SignalR notification sent successfully");
                        }
                        catch (Exception signalREx)
                        {
                            _logger.LogError(signalREx, "[NotificationService] Error sending SignalR notification. Will continue as DB save was successful");
                        }

                        _logger.LogInformation("[NotificationService] Notification created and sent successfully. NotificationId: {NotificationId}, UserId: {UserId}, Title: {Title}", 
                            notificationId, userId, title);
                        return true;
                    }

                    _logger.LogWarning("[NotificationService] Failed to create notification. UserId: {UserId}, No notification ID returned", userId);
                    await transaction.RollbackAsync();
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[NotificationService] Error in transaction. Rolling back. UserId: {UserId}", userId);
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationService] Error creating notification. UserId: {UserId}, SQL Error: {ErrorMessage}", 
                    userId, ex.Message);
                throw;
            }
        }

        public async Task<bool> CreateNotificationForRoleAsync(int roleId, string title, string message, string type)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(type);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var users = (await connection.QueryAsync<int>(
                    "SELECT USER_ID FROM USER_ROLES WHERE ROLE_ID = @RoleId",
                    new { RoleId = roleId })).AsList();

                var success = true;
                foreach (var userId in users)
                {
                    var result = await CreateNotificationAsync(userId, title, message, type);
                    if (!result) success = false;
                }

                _logger.LogInformation("Notifications created for role {RoleId}: {Title}", roleId, title);
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notifications for role {RoleId}", roleId);
                return false;
            }
        }

        public async Task<List<NotificationDto>> GetUserNotificationsAsync(int userId, bool unreadOnly = false)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT 
                        NOTIFICATION_ID as NotificationId,
                        USER_ID as UserId,
                        TITLE as Title,
                        MESSAGE as Message,
                        TYPE as Type,
                        IS_READ as IsRead,
                        CREATED_AT as CreatedAt
                    FROM NOTIFICATIONS 
                    WHERE USER_ID = @UserId
                    AND (@UnreadOnly = 0 OR (@UnreadOnly = 1 AND IS_READ = 0))
                    ORDER BY CREATED_AT DESC";

                var notifications = await connection.QueryAsync<NotificationDto>(sql, new { UserId = userId, UnreadOnly = unreadOnly });
                return notifications.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting notifications for user {UserId}", userId);
                return new List<NotificationDto>();
            }
        }

        public async Task<bool> MarkAsReadAsync(int notificationId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE NOTIFICATIONS 
                    SET IS_READ = 1 
                    WHERE NOTIFICATION_ID = @NotificationId";

                var result = await connection.ExecuteAsync(sql, new { NotificationId = notificationId });
                if (result > 0)
                {
                    _logger.LogInformation("Notification {NotificationId} marked as read", notificationId);
                }
                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read", notificationId);
                return false;
            }
        }

        public async Task<bool> MarkAllAsReadAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    UPDATE NOTIFICATIONS
                    SET IS_READ = 1
                    WHERE USER_ID = @UserId";

                var result = await connection.ExecuteAsync(sql, new { UserId = userId });
                if (result > 0)
                {
                    _logger.LogInformation("All notifications marked as read for user {UserId}", userId);
                }
                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read for user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> DeleteNotificationAsync(int notificationId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = "DELETE FROM NOTIFICATIONS WHERE NOTIFICATION_ID = @NotificationId";
                var result = await connection.ExecuteAsync(sql, new { NotificationId = notificationId });
                if (result > 0)
                {
                    _logger.LogInformation("Notification {NotificationId} deleted", notificationId);
                }
                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification {NotificationId}", notificationId);
                return false;
            }
        }

        public async Task<int> GetUnreadCountAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"
                    SELECT COUNT(1)
                    FROM NOTIFICATIONS
                    WHERE USER_ID = @UserId
                    AND IS_READ = 0";

                return await connection.ExecuteScalarAsync<int>(sql, new { UserId = userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread count for user {UserId}", userId);
                return 0;
            }
        }
    }
} 