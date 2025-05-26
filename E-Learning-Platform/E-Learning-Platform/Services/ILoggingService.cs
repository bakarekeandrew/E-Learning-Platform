using System;

namespace E_Learning_Platform.Services
{
    public interface ILoggingService
    {
        void LogInfo(string message);
        void LogInfo(string category, string message);
        void LogInfo<T>(string message, T param);
        void LogInfo<T1, T2>(string message, T1 param1, T2 param2);
        void LogInfo<T1, T2, T3>(string message, T1 param1, T2 param2, T3 param3);
        void LogInfo<T1, T2, T3, T4>(string message, T1 param1, T2 param2, T3 param3, T4 param4);
        void LogWarning(string message);
        void LogWarning(string category, string message);
        void LogWarning<T>(string message, T param);
        void LogWarning<T1, T2>(string message, T1 param1, T2 param2);
        void LogError(string message);
        void LogError(string category, string message);
        void LogError(string message, Exception? ex);
        void LogError<T>(string message, T param);
        void LogError<T1, T2>(string message, T1 param1, T2 param2);
        void LogError<T1, T2, T3>(string message, T1 param1, T2 param2, T3 param3);
        void LogError<T1, T2, T3, T4>(string message, T1 param1, T2 param2, T3 param3, T4 param4);
        void LogError(string category, string message, Exception? ex);
    }
} 