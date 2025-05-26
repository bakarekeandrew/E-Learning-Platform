using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Services
{
    public class NotificationEventService : INotificationEventService
    {
        private readonly ILogger<NotificationEventService> _logger;
        private event EventHandler<NotificationEvent>? OnNotificationRequired;
        private int _subscriberCount = 0;

        public NotificationEventService(ILogger<NotificationEventService> logger)
        {
            _logger = logger;
            _logger.LogInformation("[NotificationEventService] Service initialized");
        }

        public void Subscribe(EventHandler<NotificationEvent> handler)
        {
            try
            {
                _logger.LogDebug("[NotificationEventService] Attempting to subscribe new handler. Current subscriber count: {Count}", _subscriberCount);
                
                if (handler == null)
                {
                    _logger.LogError("[NotificationEventService] Cannot subscribe null handler");
                    throw new ArgumentNullException(nameof(handler));
                }

                // Check if handler is already subscribed
                var delegates = OnNotificationRequired?.GetInvocationList();
                if (delegates != null && delegates.Contains(handler))
                {
                    _logger.LogWarning("[NotificationEventService] Handler already subscribed");
                    return;
                }

                OnNotificationRequired += handler;
                _subscriberCount++;
                _logger.LogInformation("[NotificationEventService] Handler subscribed successfully. New subscriber count: {Count}", _subscriberCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationEventService] Error subscribing handler");
                throw;
            }
        }

        public void Unsubscribe(EventHandler<NotificationEvent> handler)
        {
            try
            {
                _logger.LogDebug("[NotificationEventService] Attempting to unsubscribe handler. Current subscriber count: {Count}", _subscriberCount);
                
                if (handler == null)
                {
                    _logger.LogError("[NotificationEventService] Cannot unsubscribe null handler");
                    throw new ArgumentNullException(nameof(handler));
                }

                OnNotificationRequired -= handler;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                _logger.LogInformation("[NotificationEventService] Handler unsubscribed successfully. New subscriber count: {Count}", _subscriberCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationEventService] Error unsubscribing handler");
                throw;
            }
        }

        public void RaiseEvent(NotificationEvent notification)
        {
            try
            {
                _logger.LogInformation("[NotificationEventService] Attempting to raise notification event. Title: {Title}, Type: {Type}, UserId: {UserId}", 
                    notification.Title, notification.Type, notification.UserId);

                if (notification == null)
                {
                    _logger.LogError("[NotificationEventService] Cannot raise null notification event");
                    throw new ArgumentNullException(nameof(notification));
                }

                if (OnNotificationRequired == null)
                {
                    _logger.LogWarning("[NotificationEventService] No handlers subscribed to handle the notification. Subscriber count: {Count}", _subscriberCount);
                    return;
                }

                var delegates = OnNotificationRequired.GetInvocationList();
                _logger.LogDebug("[NotificationEventService] Found {Count} handlers to notify", delegates.Length);

                foreach (var del in delegates)
                {
                    try
                    {
                        _logger.LogDebug("[NotificationEventService] Invoking handler {HandlerType}", del.Method.Name);
                        del.DynamicInvoke(this, notification);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[NotificationEventService] Error invoking handler {HandlerType}", del.Method.Name);
                        // Continue with other handlers even if one fails
                    }
                }

                _logger.LogInformation("[NotificationEventService] Successfully raised notification event to all handlers");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NotificationEventService] Error raising notification event for user {UserId}", 
                    notification?.UserId ?? 0);
                throw;
            }
        }
    }
} 