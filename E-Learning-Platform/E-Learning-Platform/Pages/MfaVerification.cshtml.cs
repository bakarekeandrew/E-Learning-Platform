using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using E_Learning_Platform.Pages.Services;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace E_Learning_Platform.Pages
{
    public class MfaVerificationModel : PageModel
    {
        [BindProperty]
        [Required]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; }

        public string Email { get; set; }
        public string ErrorMessage { get; set; }
        public string InfoMessage { get; set; }

        private readonly OtpService _otpService;
        private readonly EmailService _emailService;
        private readonly ILogger<MfaVerificationModel> _logger;

        public MfaVerificationModel(
            OtpService otpService,
            EmailService emailService,
            ILogger<MfaVerificationModel> logger)
        {
            _otpService = otpService;
            _emailService = emailService;
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            if (TempData["PendingUserId"] == null)
            {
                _logger.LogWarning("No pending user ID found in TempData");
                return RedirectToPage("/Login");
            }

            Email = TempData["PendingEmail"]?.ToString();

            TempData.Keep("PendingUserId");
            TempData.Keep("PendingEmail");
            TempData.Keep("PendingUserRole");
            TempData.Keep("PendingUserName");
            TempData.Keep("RememberMe");

            InfoMessage = $"Verification code has been sent to {Email}";
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Email = TempData["PendingEmail"]?.ToString();
                TempData.Keep("PendingEmail");
                return Page();
            }

            if (TempData["PendingUserId"] == null)
            {
                _logger.LogWarning("No pending user ID found in TempData during POST");
                ErrorMessage = "Your session has expired. Please log in again.";
                return RedirectToPage("/Login");
            }

            int userId = (int)TempData["PendingUserId"];
            string email = TempData["PendingEmail"]?.ToString();
            string role = TempData["PendingUserRole"]?.ToString();
            string fullName = TempData["PendingUserName"]?.ToString();
            bool rememberMe = TempData["RememberMe"] != null && (bool)TempData["RememberMe"];

            try
            {
                bool isValid = _otpService.ValidateOtp(userId, VerificationCode);

                if (isValid)
                {
                    _otpService.MarkOtpAsUsed(userId, VerificationCode);

                    HttpContext.Session.SetInt32("UserId", userId);
                    HttpContext.Session.SetString("UserRole", role);
                    HttpContext.Session.SetString("UserName", fullName);

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.Name, fullName),
                        new Claim(ClaimTypes.Email, email),
                        new Claim(ClaimTypes.Role, role),
                        new Claim("UserId", userId.ToString())
                    };

                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = rememberMe,
                        ExpiresUtc = rememberMe
                            ? DateTimeOffset.UtcNow.AddDays(30)
                            : DateTimeOffset.UtcNow.AddHours(1)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                        authProperties);

                    return RedirectToPage(role switch
                    {
                        "INSTRUCTOR" => "/Instructor/Dashboard",
                        "ADMIN" => "/AdminDashboard",
                        "STUDENT" => "/Student/Dashboard",
                        _ => "/Login"
                    });
                }

                ErrorMessage = "Invalid verification code. Please try again.";
                TempData.Keep();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during MFA verification for user ID: {userId}");
                ErrorMessage = $"Verification error: {ex.Message}";
                TempData.Keep();
                return Page();
            }
        }

        public IActionResult OnPostResendCode()
        {
            if (TempData["PendingUserId"] == null || TempData["PendingEmail"] == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                int userId = (int)TempData["PendingUserId"];
                string email = TempData["PendingEmail"].ToString();

                var otp = _otpService.GenerateOtp();
                _otpService.SaveOtp(userId, otp);
                _emailService.SendOtpEmail(email, otp);

                TempData.Keep();
                InfoMessage = "A new verification code has been sent to your email address.";
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending OTP");
                ErrorMessage = $"Error resending verification code: {ex.Message}";
                TempData.Keep();
                return Page();
            }
        }
    }
}