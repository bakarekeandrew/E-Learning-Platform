namespace E_Learning_Platform.Pages.Services
{
    public class ConfigurationService
    {
        // Database configuration
        public string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                         "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                         "Integrated Security=True;" +
                                         "TrustServerCertificate=True;" +
                                         "Encrypt=True;" +
                                         "Connection Timeout=30;";

        // Email configuration
        public string SmtpServer => "smtp.gmail.com";
        public int SmtpPort => 587;
        public string SmtpUsername => "bakarekeandrew@gmail.com";
        public string SmtpPassword => "nclc evob tgdr wvuf"; // Use app password
        public string FromEmail => "bakarekeandrew@gmail.com";
        public string FromName => "E-Learning Platform";

        // OTP configuration
        public int OtpExpirationMinutes => 10;
        public int OtpLength => 6;
    }
}