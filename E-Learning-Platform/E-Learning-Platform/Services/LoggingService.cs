using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace E_Learning_Platform.Services
{
    public class LoggingService : ILoggingService
    {
        private readonly string _connectionString;
        private readonly ILogger<LoggingService> _logger;

        public LoggingService(IConfiguration configuration, ILogger<LoggingService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        public void LogInfo(string message)
        {
            _logger.LogInformation(message);
        }

        public void LogInfo(string category, string message)
        {
            _logger.LogInformation("[{Category}] {Message}", category, message);
        }

        public void LogInfo<T>(string message, T param)
        {
            _logger.LogInformation(message, param);
        }

        public void LogInfo<T1, T2>(string message, T1 param1, T2 param2)
        {
            _logger.LogInformation(message, param1, param2);
        }

        public void LogInfo<T1, T2, T3>(string message, T1 param1, T2 param2, T3 param3)
        {
            _logger.LogInformation(message, param1, param2, param3);
        }

        public void LogInfo<T1, T2, T3, T4>(string message, T1 param1, T2 param2, T3 param3, T4 param4)
        {
            _logger.LogInformation(message, param1, param2, param3, param4);
        }

        public void LogWarning(string message)
        {
            _logger.LogWarning(message);
        }

        public void LogWarning(string category, string message)
        {
            _logger.LogWarning("[{Category}] {Message}", category, message);
        }

        public void LogWarning<T>(string message, T param)
        {
            _logger.LogWarning(message, param);
        }

        public void LogWarning<T1, T2>(string message, T1 param1, T2 param2)
        {
            _logger.LogWarning(message, param1, param2);
        }

        public void LogError(string message)
        {
            _logger.LogError(message);
        }

        public void LogError(string category, string message)
        {
            _logger.LogError("[{Category}] {Message}", category, message);
        }

        public void LogError(string message, Exception? ex)
        {
            _logger.LogError(ex, message);
        }

        public void LogError<T>(string message, T param)
        {
            _logger.LogError(message, param);
        }

        public void LogError<T1, T2>(string message, T1 param1, T2 param2)
        {
            _logger.LogError(message, param1, param2);
        }

        public void LogError<T1, T2, T3>(string message, T1 param1, T2 param2, T3 param3)
        {
            _logger.LogError(message, param1, param2, param3);
        }

        public void LogError<T1, T2, T3, T4>(string message, T1 param1, T2 param2, T3 param3, T4 param4)
        {
            _logger.LogError(message, param1, param2, param3, param4);
        }

        public void LogError(string category, string message, Exception? ex)
        {
            _logger.LogError(ex, "[{Category}] {Message}", category, message);
        }

        private void Log(string level, string category, string message)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var sql = @"
                    INSERT INTO SYSTEM_LOGS (LOG_LEVEL, CATEGORY, MESSAGE, LOG_DATE)
                    VALUES (@Level, @Category, @Message, GETDATE())";

                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@Level", level);
                command.Parameters.AddWithValue("@Category", category);
                command.Parameters.AddWithValue("@Message", message);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // If logging fails, write to console as a last resort
                Console.WriteLine($"Failed to log to database: {ex.Message}");
                Console.WriteLine($"Original log: [{level}] {category}: {message}");
            }
        }
    }
} 