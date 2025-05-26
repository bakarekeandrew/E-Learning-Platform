using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Dapper;
using E_Learning_Platform.Services;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages
{
    [Authorize]
    public class UserProfileModel : PageModel
    {
        public class ProfileViewModel
        {
            public int UserId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool MfaEnabled { get; set; }
        }

        [BindProperty]
        public ProfileViewModel Profile { get; set; } = new ProfileViewModel();

        [BindProperty]
        [Display(Name = "Enable Two-Factor Authentication")]
        public bool EnableMfa { get; set; }

        [TempData]
        public string StatusMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Please enter the verification code")]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; } = string.Empty;

        public bool ShowVerificationForm { get; set; }

        private readonly ILoggingService _logger;
        private readonly string _connectionString;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;

        public UserProfileModel(
            IConfiguration configuration,
            ILoggingService logger,
            IOtpService otpService,
            IEmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _otpService = otpService;
            _emailService = emailService;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInfo("UserProfile", "No UserId claim found in user principal");
                return RedirectToPage("/Login");
            }

            _logger.LogInfo("UserProfile", $"Loading profile for user ID: {userId}");
            await LoadUserProfileAsync(int.Parse(userId));
            EnableMfa = Profile.MfaEnabled;
            ShowVerificationForm = TempData["ShowVerificationForm"] != null && (bool)TempData["ShowVerificationForm"];

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInfo("UserProfile", "No UserId claim found in user principal during post");
                return RedirectToPage("/Login");
            }

            var userIdInt = int.Parse(userId);
            _logger.LogInfo("UserProfile", $"Processing profile update for user ID: {userIdInt}");
            await LoadUserProfileAsync(userIdInt);

            // If MFA status has changed
            if (EnableMfa != Profile.MfaEnabled)
            {
                if (EnableMfa)
                {
                    _logger.LogInfo("UserProfile", $"User {userIdInt} is enabling MFA");

                    // Enable MFA - Send verification code first
                    try
                    {
                        // Generate and save OTP
                        var otp = _otpService.GenerateOtp();
                        _otpService.SaveOtp(userIdInt, otp);

                        // Send email with OTP
                        _emailService.SendOtpEmail(Profile.Email, otp);

                        _logger.LogInfo("UserProfile", $"Sent verification code to {Profile.Email} for user {userIdInt}");

                        TempData["ShowVerificationForm"] = true;
                        StatusMessage = "Please verify your email by entering the code we just sent.";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error sending verification code", ex);
                        ErrorMessage = $"Error sending verification code: {ex.Message}";
                        return RedirectToPage();
                    }

                    return RedirectToPage();
                }
                else
                {
                    _logger.LogInfo("UserProfile", $"User {userIdInt} is disabling MFA");

                    // Disable MFA - Do it directly
                    try
                    {
                        using var connection = new SqlConnection(_connectionString);
                        await connection.ExecuteAsync(
                            "UPDATE USERS SET MFA_ENABLED = 0 WHERE USER_ID = @UserId",
                            new { UserId = userIdInt });

                        _logger.LogInfo("UserProfile", $"Successfully disabled MFA for user {userIdInt}");
                        StatusMessage = "Two-factor authentication has been disabled.";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error disabling MFA", ex);
                        ErrorMessage = $"Error: {ex.Message}";
                    }
                }
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostVerifyAsync()
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInfo("UserProfile", "No UserId claim found in user principal during verification");
                return RedirectToPage("/Login");
            }

            var userIdInt = int.Parse(userId);
            _logger.LogInfo("UserProfile", $"Verifying OTP for user {userIdInt}");
            await LoadUserProfileAsync(userIdInt);

            if (!ModelState.IsValid)
            {
                _logger.LogInfo("UserProfile", $"Invalid model state during OTP verification for user {userIdInt}");
                ShowVerificationForm = true;
                return Page();
            }

            _logger.LogInfo("UserProfile", $"Validating OTP code '{VerificationCode}' for user {userIdInt}");

            // Verify OTP code
            try
            {
                bool isValid = await _otpService.ValidateOtpAsync(userIdInt, VerificationCode);

                if (isValid)
                {
                    _logger.LogInfo("UserProfile", $"OTP validation successful for user {userIdInt}");

                    // Mark OTP as used
                    _otpService.MarkOtpAsUsed(userIdInt, VerificationCode);

                    // Enable MFA for user
                    try
                    {
                        using var connection = new SqlConnection(_connectionString);
                        await connection.ExecuteAsync(
                            "UPDATE USERS SET MFA_ENABLED = 1 WHERE USER_ID = @UserId",
                            new { UserId = userIdInt });

                        _logger.LogInfo("UserProfile", $"Successfully enabled MFA for user {userIdInt}");
                        StatusMessage = "Two-factor authentication has been enabled successfully.";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Database error enabling MFA", ex);
                        ErrorMessage = $"Error: {ex.Message}";
                        ShowVerificationForm = true;
                        return Page();
                    }
                }
                else
                {
                    _logger.LogInfo("UserProfile", $"Invalid OTP code '{VerificationCode}' for user {userIdInt}");
                    ModelState.AddModelError("VerificationCode", "Invalid or expired verification code");
                    ShowVerificationForm = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error during OTP validation", ex);
                ModelState.AddModelError("VerificationCode", $"Error validating code: {ex.Message}");
                ShowVerificationForm = true;
                return Page();
            }

            return RedirectToPage();
        }

        private async Task LoadUserProfileAsync(int userId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                var user = await connection.QueryFirstOrDefaultAsync<ProfileViewModel>(
                    @"SELECT U.USER_ID AS UserId, 
                      U.FULL_NAME AS FullName, 
                      U.EMAIL AS Email, 
                      R.ROLE_NAME AS Role, 
                      U.MFA_ENABLED AS MfaEnabled 
                      FROM USERS U 
                      JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID 
                      WHERE U.USER_ID = @UserId",
                    new { UserId = userId });

                if (user != null)
                {
                    Profile = user;
                    _logger.LogInfo("UserProfile", $"Successfully loaded profile for user {userId}");
                }
                else
                {
                    _logger.LogError($"No user found with ID {userId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error loading profile", ex);
                ErrorMessage = $"Error loading profile: {ex.Message}";
            }
        }

        public IActionResult OnPostResendCode()
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogInfo("UserProfile", "No UserId claim found when resending code");
                return RedirectToPage("/Login");
            }

            var userIdInt = int.Parse(userId);
            _logger.LogInfo("UserProfile", $"Resending OTP for user {userIdInt}");

            try
            {
                // Load user profile to get email
                using var connection = new SqlConnection(_connectionString);
                var user = connection.QueryFirstOrDefault<ProfileViewModel>(
                    @"SELECT U.USER_ID AS UserId, U.EMAIL AS Email
                      FROM USERS U WHERE U.USER_ID = @UserId",
                    new { UserId = userIdInt });

                if (user != null)
                {
                    // Generate and send new OTP
                    var otp = _otpService.GenerateOtp();
                    _otpService.SaveOtp(userIdInt, otp);
                    _emailService.SendOtpEmail(user.Email, otp);

                    _logger.LogInfo("UserProfile", $"Successfully resent OTP to {user.Email} for user {userIdInt}");

                    TempData["ShowVerificationForm"] = true;
                    StatusMessage = "A new verification code has been sent to your email.";
                }
                else
                {
                    _logger.LogError($"User not found when resending OTP: {userIdInt}");
                    ErrorMessage = "User profile not found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error resending OTP", ex);
                ErrorMessage = $"Error sending verification code: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}