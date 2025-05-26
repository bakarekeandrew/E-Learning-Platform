using System.Threading.Tasks;

namespace E_Learning_Platform.Services
{
    public interface IOtpService
    {
        string GenerateOtp();
        Task<bool> ValidateOtpAsync(int userId, string otp);
        Task<bool> ValidateTempUserOtpAsync(int tempUserId, string otpCode);
        void SaveOtp(int userId, string otp);
        void MarkOtpAsUsed(int userId, string otpCode);
    }
} 