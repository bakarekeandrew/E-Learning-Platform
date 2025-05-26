using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface IEmailService
    {
        bool SendOtpEmail(string email, string otp);
        Task<bool> SendOtpEmailAsync(string email, string otp);
        bool SendEmail(string to, string subject, string body);
        Task<bool> SendEmailAsync(string to, string subject, string body);
        Task SendPasswordResetEmailAsync(string recipientEmail, string resetToken);
    }
} 