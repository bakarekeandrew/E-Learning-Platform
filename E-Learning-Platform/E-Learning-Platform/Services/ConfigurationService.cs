using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetConnectionString()
        {
            return _configuration.GetConnectionString("DefaultConnection") ?? 
                "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                "Integrated Security=True;" +
                "TrustServerCertificate=True;" +
                "Encrypt=True;" +
                "Connection Timeout=30;";
        }

        public T GetValue<T>(string key)
        {
            return _configuration.GetValue<T>(key);
        }

        public T GetValue<T>(string key, T defaultValue)
        {
            return _configuration.GetValue<T>(key) ?? defaultValue;
        }

        public void SetValue<T>(string key, T value)
        {
            // Since IConfiguration is read-only by default, we'll need to implement this
            // if you need to modify configuration at runtime
            throw new NotImplementedException("SetValue is not supported in the current implementation");
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            try
            {
                value = _configuration.GetValue<T>(key);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public Task ReloadAsync()
        {
            // If using IConfigurationRoot, you can reload
            if (_configuration is IConfigurationRoot configRoot)
            {
                return Task.Run(() => configRoot.Reload());
            }
            return Task.CompletedTask;
        }

        // Email configuration
        public string SmtpServer => GetValue<string>("Smtp:Server", "smtp.gmail.com");
        public int SmtpPort => GetValue<int>("Smtp:Port", 587);
        public string SmtpUsername => GetValue<string>("Smtp:Username", "bakarekeandrew@gmail.com");
        public string SmtpPassword => GetValue<string>("Smtp:Password", "nclc evob tgdr wvuf"); // Use app password
        public string FromEmail => GetValue<string>("Email:From", "bakarekeandrew@gmail.com");
        public string FromName => GetValue<string>("Email:FromName", "E-Learning Platform");

        // OTP configuration
        public int OtpExpirationMinutes => GetValue<int>("Otp:ExpirationMinutes", 10);
        public int OtpLength => GetValue<int>("Otp:Length", 6);
    }
}