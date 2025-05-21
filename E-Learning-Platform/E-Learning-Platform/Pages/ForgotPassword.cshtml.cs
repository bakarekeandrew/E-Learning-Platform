using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using E_Learning_Platform.Pages.Services;
using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Pages
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly LoggingService _logger = new LoggingService();
        private readonly OtpService _otpService;
        private readonly EmailService _emailService;

        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                         "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                         "Integrated Security=True;" +
                                         "TrustServerCertificate=True";

        [BindProperty]
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; } = string.Empty;

        [BindProperty]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("NewPassword", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        public string ErrorMessage { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public bool ShowEmailForm { get; set; } = true;
        public bool ShowVerificationForm { get; set; } = false;
        public bool ShowResetForm { get; set; } = false;

        public ForgotPasswordModel()
        {
            _logger.LogInfo("ForgotPasswordModel", "Initializing ForgotPasswordModel");
            _otpService = new OtpService(ConnectionString);
            _emailService = new EmailService();
        }

        public void OnGet()
        {
            _logger.LogInfo("ForgotPasswordModel", "Loading forgot password page");
            ShowEmailForm = true;
            ShowVerificationForm = false;
            ShowResetForm = false;
        }

        public async Task<IActionResult> OnPostRequestResetAsync()
        {
            _logger.LogInfo("OnPostRequestReset", "Method started");

            // Only validate Email for this step
            if (!ModelState.IsValid)
            {
                var emailErrors = ModelState
                    .Where(x => x.Key == nameof(Email) || x.Key == string.Empty)
                    .SelectMany(x => x.Value.Errors);

                if (emailErrors.Any())
                {
                    _logger.LogInfo("OnPostRequestReset", $"Email validation failed: {string.Join(", ", emailErrors.Select(e => e.ErrorMessage))}");
                    ShowEmailForm = true;
                    return Page();
                }
            }

            try
            {
                _logger.LogInfo("OnPostRequestReset", $"Checking email existence: {Email}");
                if (!await UserEmailExistsAsync(Email))
                {
                    _logger.LogInfo("OnPostRequestReset", $"Email not found: {Email}");
                    StatusMessage = $"If an account with email {Email} exists, a verification code has been sent.";
                    ShowEmailForm = true;
                    return Page();
                }

                _logger.LogInfo("OnPostRequestReset", $"Email found, getting user ID");
                int userId = await GetUserIdFromEmailAsync(Email);
                _logger.LogInfo("OnPostRequestReset", $"User ID found: {userId}");

                var otp = _otpService.GenerateOtp();
                _logger.LogInfo("OnPostRequestReset", $"Generated OTP: {otp}");
                _otpService.SaveOtp(userId, otp);

                _logger.LogInfo("OnPostRequestReset", "Attempting to send email");
                bool emailSent = _emailService.SendOtpEmail(Email, otp);

                if (emailSent)
                {
                    _logger.LogInfo("OnPostRequestReset", "Email sent successfully");
                    TempData["ResetEmail"] = Email;
                    TempData["UserId"] = userId;

                    StatusMessage = $"A verification code has been sent to {Email}. Please check your inbox.";
                    ShowEmailForm = false;
                    ShowVerificationForm = true;
                    return Page();
                }
                else
                {
                    _logger.LogError("OnPostRequestReset", "Failed to send email");
                    ErrorMessage = "Failed to send verification email. Please try again.";
                    ShowEmailForm = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("OnPostRequestReset", $"Exception occurred: {ex.Message}", ex);
                ErrorMessage = "An error occurred. Please try again.";
                ShowEmailForm = true;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostVerifyCodeAsync()
        {
            _logger.LogInfo("ForgotPasswordModel", "Verifying reset code");

            // Only validate verification code for this step
            if (string.IsNullOrEmpty(VerificationCode))
            {
                _logger.LogInfo("ForgotPasswordModel", "Verification code is empty");
                ModelState.AddModelError("VerificationCode", "Please enter the verification code");
                ShowVerificationForm = true;
                return Page();
            }

            try
            {
                // Retrieve email and user ID from TempData
                string email = TempData["ResetEmail"]?.ToString();
                int? userId = TempData["UserId"] as int?;

                if (string.IsNullOrEmpty(email) || !userId.HasValue)
                {
                    _logger.LogError("ForgotPasswordModel", "Email or user ID not found in TempData");
                    ErrorMessage = "Session expired. Please start the password reset process again.";
                    ShowEmailForm = true;
                    return Page();
                }

                // Preserve values for potential failed verification
                TempData["ResetEmail"] = email;
                TempData["UserId"] = userId;
                Email = email;

                _logger.LogInfo("ForgotPasswordModel", $"Validating OTP for user: {userId}");

                // Validate OTP
                bool isValid = _otpService.ValidateOtp(userId.Value, VerificationCode);
                _logger.LogInfo("ForgotPasswordModel", $"OTP validation result: {isValid}");

                if (isValid)
                {
                    _logger.LogInfo("ForgotPasswordModel", "OTP validation successful");
                    // Mark OTP as used to prevent reuse
                    _otpService.MarkOtpAsUsed(userId.Value, VerificationCode);

                    // Store verification in TempData for password reset stage
                    TempData["VerifiedForReset"] = true;

                    // Show password reset form
                    ShowEmailForm = false;
                    ShowVerificationForm = false;
                    ShowResetForm = true;
                    StatusMessage = "Verification successful. Please enter your new password.";
                    return Page();
                }
                else
                {
                    _logger.LogInfo("ForgotPasswordModel", "Invalid OTP provided");
                    ModelState.AddModelError("VerificationCode", "Invalid or expired verification code");
                    ShowVerificationForm = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", "Error verifying code", ex);
                ErrorMessage = "An error occurred during verification. Please try again.";
                ShowVerificationForm = true;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostResetPasswordAsync()
        {
            _logger.LogInfo("ForgotPasswordModel", "Processing password reset");

            // Validate password fields for this step
            if (string.IsNullOrEmpty(NewPassword))
            {
                ModelState.AddModelError("NewPassword", "New Password is required");
            }
            else if (NewPassword.Length < 6)
            {
                ModelState.AddModelError("NewPassword", "Password must be at least 6 characters long");
            }

            if (NewPassword != ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "The password and confirmation password do not match");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogInfo("ForgotPasswordModel", "Invalid model state for password reset");
                ShowResetForm = true;
                return Page();
            }

            try
            {
                // Verify that user is verified for reset
                if (TempData["VerifiedForReset"] == null || !(bool)TempData["VerifiedForReset"])
                {
                    _logger.LogError("ForgotPasswordModel", "User not verified for password reset");
                    ErrorMessage = "Verification required. Please start the password reset process again.";
                    ShowEmailForm = true;
                    return Page();
                }

                // Get email and user ID from TempData
                string email = TempData["ResetEmail"]?.ToString();
                int? userId = TempData["UserId"] as int?;

                if (string.IsNullOrEmpty(email) || !userId.HasValue)
                {
                    _logger.LogError("ForgotPasswordModel", "Email or user ID not found in TempData");
                    ErrorMessage = "Session expired. Please start the password reset process again.";
                    ShowEmailForm = true;
                    return Page();
                }

                // Hash the new password
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                _logger.LogInfo("ForgotPasswordModel", "New password hashed");

                // Update password in database
                bool updated = await UpdateUserPasswordAsync(userId.Value, passwordHash);
                _logger.LogInfo("ForgotPasswordModel", $"Password update result: {updated}");

                if (updated)
                {
                    _logger.LogInfo("ForgotPasswordModel", "Password reset successful");
                    // Clear TempData
                    TempData.Remove("ResetEmail");
                    TempData.Remove("UserId");
                    TempData.Remove("VerifiedForReset");

                    // Redirect to login with success message
                    TempData["ResetSuccess"] = "Your password has been reset successfully. Please log in with your new password.";
                    return RedirectToPage("/Login");
                }
                else
                {
                    _logger.LogError("ForgotPasswordModel", "Failed to update password");
                    ErrorMessage = "Failed to update password. Please try again.";
                    ShowResetForm = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", "Error resetting password", ex);
                ErrorMessage = "An error occurred while resetting your password. Please try again.";
                ShowResetForm = true;
                return Page();
            }
        }

        public IActionResult OnPostResendCodeAsync()
        {
            _logger.LogInfo("ForgotPasswordModel", "Resending verification code");

            string email = TempData["ResetEmail"]?.ToString();
            int? userId = TempData["UserId"] as int?;

            if (string.IsNullOrEmpty(email) || !userId.HasValue)
            {
                _logger.LogError("ForgotPasswordModel", "Email or user ID not found in TempData for resend");
                ErrorMessage = "Session expired. Please start the password reset process again.";
                ShowEmailForm = true;
                return Page();
            }

            try
            {
                // Preserve for form
                TempData["ResetEmail"] = email;
                TempData["UserId"] = userId;
                Email = email;

                // Generate new OTP
                var otp = _otpService.GenerateOtp();
                _logger.LogInfo("ForgotPasswordModel", $"Generated new OTP for resend: {otp}");
                _otpService.SaveOtp(userId.Value, otp);

                // Send email with OTP
                bool emailSent = _emailService.SendOtpEmail(email, otp);
                _logger.LogInfo("ForgotPasswordModel", $"Resend email result: {emailSent}");

                if (emailSent)
                {
                    StatusMessage = "A new verification code has been sent to your email address.";
                }
                else
                {
                    ErrorMessage = "Failed to send verification email. Please try again.";
                }

                ShowVerificationForm = true;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", "Error resending verification code", ex);
                ErrorMessage = "An error occurred while resending the code. Please try again.";
                ShowVerificationForm = true;
                return Page();
            }
        }

        private async Task<bool> UserEmailExistsAsync(string email)
        {
            _logger.LogInfo("ForgotPasswordModel", $"Checking if email exists: {email}");
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT COUNT(*) FROM USERS WHERE EMAIL = @Email", connection);
                    command.Parameters.AddWithValue("@Email", email);

                    var exists = (int)await command.ExecuteScalarAsync() > 0;
                    _logger.LogInfo("ForgotPasswordModel", $"Email exists check result: {exists}");
                    return exists;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", "Error checking email existence", ex);
                throw;
            }
        }

        private async Task<int> GetUserIdFromEmailAsync(string email)
        {
            _logger.LogInfo("ForgotPasswordModel", $"Getting user ID for email: {email}");
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT USER_ID FROM USERS WHERE EMAIL = @Email", connection);
                    command.Parameters.AddWithValue("@Email", email);

                    var result = await command.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                    {
                        throw new Exception($"No user found with email: {email}");
                    }

                    int userId = Convert.ToInt32(result);
                    _logger.LogInfo("ForgotPasswordModel", $"Found user ID: {userId}");
                    return userId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", $"Error getting user ID for email: {email}", ex);
                throw;
            }
        }

        private async Task<bool> UpdateUserPasswordAsync(int userId, string passwordHash)
        {
            _logger.LogInfo("ForgotPasswordModel", $"Updating password for user ID: {userId}");
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "UPDATE USERS SET PASSWORD_HASH = @PasswordHash WHERE USER_ID = @UserId", connection);
                    command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                    command.Parameters.AddWithValue("@UserId", userId);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    bool success = rowsAffected > 0;
                    _logger.LogInfo("ForgotPasswordModel", $"Password update rows affected: {rowsAffected}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ForgotPasswordModel", $"Error updating password for user ID: {userId}", ex);
                throw;
            }
        }
    }
}