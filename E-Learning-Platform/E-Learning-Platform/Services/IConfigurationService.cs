using System;
using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface IConfigurationService
    {
        string GetConnectionString();
        T GetValue<T>(string key);
        T GetValue<T>(string key, T defaultValue);
        void SetValue<T>(string key, T value);
        bool TryGetValue<T>(string key, out T value);
        Task ReloadAsync();
    }
} 