using Microsoft.Extensions.Logging;
using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace E_Learning_Platform.Pages.Services
{
    public class LoggingService : ILogger
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();
        private readonly IWebHostEnvironment _env;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 100;

        public LoggingService()
        {
            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        public LoggingService(IWebHostEnvironment env)
        {
            _env = env;
            var logDirectory = Path.Combine(_env.ContentRootPath, "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, $"log_{DateTime.Now:yyyyMMdd}.txt");
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {formatter(state, exception)}";
            if (exception != null)
            {
                logEntry += $"\nException: {exception.Message}\nStack Trace: {exception.StackTrace}";
            }

            WriteLogEntry(logEntry).GetAwaiter().GetResult();
        }

        private async Task WriteLogEntry(string logEntry)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                try
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        using (var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                        using (var writer = new StreamWriter(fileStream))
                        {
                            await writer.WriteLineAsync(logEntry);
                        }
                        return;
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (IOException)
                {
                    if (i == MaxRetries - 1)
                    {
                        // On last retry, try to write to a backup file
                        var backupPath = _logFilePath + ".backup";
                        try
                        {
                            using (var fileStream = new FileStream(backupPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                            using (var writer = new StreamWriter(fileStream))
                            {
                                await writer.WriteLineAsync(logEntry);
                            }
                            return;
                        }
                        catch
                        {
                            // If we can't write to the backup file either, just throw
                            throw;
                        }
                    }
                    await Task.Delay(RetryDelayMs * (i + 1));
                }
            }
        }

        public void LogInfo(string category, string message)
        {
            Log(LogLevel.Information, new EventId(), $"[{category}] {message}", null, (state, ex) => state.ToString());
        }

        public void LogWarning(string category, string message)
        {
            Log(LogLevel.Warning, new EventId(), $"[{category}] {message}", null, (state, ex) => state.ToString());
        }

        public void LogError(string category, string message)
        {
            Log(LogLevel.Error, new EventId(), $"[{category}] {message}", null, (state, ex) => state.ToString());
        }

        public void LogInformation(string message, params object[] args)
        {
            Log(LogLevel.Information, new EventId(), string.Format(message, args), null, (state, ex) => state.ToString());
        }

        public void LogWarning(string message, params object[] args)
        {
            Log(LogLevel.Warning, new EventId(), string.Format(message, args), null, (state, ex) => state.ToString());
        }

        public void LogError(string message, params object[] args)
        {
            Log(LogLevel.Error, new EventId(), string.Format(message, args), null, (state, ex) => state.ToString());
        }

        public void LogError(Exception ex, string message, params object[] args)
        {
            Log(LogLevel.Error, new EventId(), string.Format(message, args), ex, (state, exception) => state.ToString());
        }
    }
}