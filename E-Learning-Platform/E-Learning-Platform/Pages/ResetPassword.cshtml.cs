using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Dapper;
using E_Learning_Platform.Services;
using System.ComponentModel.DataAnnotations;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace E_Learning_Platform.Pages
{
    [AllowAnonymous] // Changed from [Authorize] to allow access without login
    public class ResetPasswordModel : PageModel
    {
        private readonly ILoggingService _logger;
        private readonly string _connectionString;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;

        public ResetPasswordModel(
            IConfiguration configuration,
            ILoggingService logger,
            IOtpService otpService,
            IEmailService emailService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        }

        // For logged-in users who want to change their password
        [BindProperty]
        [DataType(DataType.Password)]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "New password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long.")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = string.Empty;

        [BindProperty]
        [Required(ErrorMessage = "Please confirm your new password")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // For forgot password flow
        [BindProperty]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; } = string.Empty;

        public string ErrorMessage { get; set; } = string.Empty;
        public string SuccessMessage { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;

        // UI control flags
        public bool ShowLoginUserForm { get; set; } = false;
        public bool ShowEmailForm { get; set; } = false;
        public bool ShowVerificationForm { get; set; } = false;
        public bool ShowResetForm { get; set; } = false;
        public bool IsForgotPasswordFlow { get; set; } = false;

        public void OnGet(string mode = "")
        {
            _logger.LogInfo("ResetPasswordModel", "Reset password page loaded with mode: " + mode);

            if (mode?.ToLower() == "forgot")
            {
                // User accessed via "Forgot Password" link
                ShowEmailForm = true;
                IsForgotPasswordFlow = true;
            }
            else if (User.Identity.IsAuthenticated)
            {
                // Normal logged-in user wants to change password
                ShowLoginUserForm = true;
                IsForgotPasswordFlow = false;
            }
            else
            {
                // If no mode specified and not logged in, default to forgot password flow
                ShowEmailForm = true;
                IsForgotPasswordFlow = true;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInfo("ResetPasswordModel", "Processing standard password change request");

            if (!ModelState.IsValid)
            {
                ShowLoginUserForm = true;
                return Page();
            }

            try
            {
                // Get the current user ID from claims
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                {
                    _logger.LogError("ResetPasswordModel", "User ID not found in claims");
                    ErrorMessage = "Unable to identify the current user. Please try logging in again.";
                    ShowLoginUserForm = true;
                    return Page();
                }

                // Verify current password
                bool passwordVerified = await VerifyCurrentPasswordAsync(userId, CurrentPassword);
                if (!passwordVerified)
                {
                    _logger.LogInfo("ResetPasswordModel", "Current password verification failed");
                    ErrorMessage = "The current password is incorrect.";
                    ShowLoginUserForm = true;
                    return Page();
                }

                // If the new password is the same as the current one
                if (CurrentPassword == NewPassword)
                {
                    _logger.LogInfo("ResetPasswordModel", "New password is the same as current password");
                    ErrorMessage = "The new password must be different from your current password.";
                    ShowLoginUserForm = true;
                    return Page();
                }

                // Update password
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                bool updated = await UpdateUserPasswordAsync(userId, passwordHash);

                if (updated)
                {
                    _logger.LogInfo("ResetPasswordModel", "Password updated successfully");
                    SuccessMessage = "Your password has been changed successfully.";
                    // Clear the form
                    ModelState.Clear();
                    CurrentPassword = string.Empty;
                    NewPassword = string.Empty;
                    ConfirmPassword = string.Empty;
                    ShowLoginUserForm = true;
                    return Page();
                }
                else
                {
                    _logger.LogError("ResetPasswordModel", "Failed to update password");
                    ErrorMessage = "Failed to update password. Please try again.";
                    ShowLoginUserForm = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error processing password reset", ex);
                ErrorMessage = "An error occurred while resetting your password. Please try again.";
                ShowLoginUserForm = true;
                return Page();
            }
        }

        // Update only the method name to match the form handler
        // Change from:
        // public async Task<IActionResult> OnPostResetPasswordAsync()
        // To:
        public async Task<IActionResult> OnPostResetPassword()
        {
            _logger.LogInfo("ResetPasswordModel", "Processing password reset");

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
                _logger.LogInfo("ResetPasswordModel", "Invalid model state for password reset");
                ShowResetForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }

            try
            {
                // Verify that user is verified for reset
                if (TempData["VerifiedForReset"] == null || !(bool)TempData["VerifiedForReset"])
                {
                    _logger.LogError("ResetPasswordModel", "User not verified for password reset");
                    ErrorMessage = "Verification required. Please start the password reset process again.";
                    ShowEmailForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }

                // Get email and user ID from TempData
                string email = TempData["ResetEmail"]?.ToString();
                int? userId = TempData["UserId"] as int?;

                if (string.IsNullOrEmpty(email) || !userId.HasValue)
                {
                    _logger.LogError("ResetPasswordModel", "Email or user ID not found in TempData");
                    ErrorMessage = "Session expired. Please start the password reset process again.";
                    ShowEmailForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }

                // Hash the new password
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                _logger.LogInfo("ResetPasswordModel", "New password hashed");

                // Update password in database
                bool updated = await UpdateUserPasswordAsync(userId.Value, passwordHash);
                _logger.LogInfo("ResetPasswordModel", $"Password update result: {updated}");

                if (updated)
                {
                    _logger.LogInfo("ResetPasswordModel", "Password reset successful");
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
                    _logger.LogError("ResetPasswordModel", "Failed to update password");
                    ErrorMessage = "Failed to update password. Please try again.";
                    ShowResetForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error resetting password", ex);
                ErrorMessage = "An error occurred while resetting your password. Please try again.";
                ShowResetForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostVerifyCodeAsync()
        {
            _logger.LogInfo("ResetPasswordModel", "Verifying reset code");

            // Only validate verification code for this step
            if (string.IsNullOrEmpty(VerificationCode))
            {
                _logger.LogInfo("ResetPasswordModel", "Verification code is empty");
                ModelState.AddModelError("VerificationCode", "Please enter the verification code");
                ShowVerificationForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }

            try
            {
                // Retrieve email and user ID from TempData
                string email = TempData["ResetEmail"]?.ToString();
                int? userId = TempData["UserId"] as int?;

                if (string.IsNullOrEmpty(email) || !userId.HasValue)
                {
                    _logger.LogError("ResetPasswordModel", "Email or user ID not found in TempData");
                    ErrorMessage = "Session expired. Please start the password reset process again.";
                    ShowEmailForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }

                // Preserve values for potential failed verification
                TempData["ResetEmail"] = email;
                TempData["UserId"] = userId;
                Email = email;

                _logger.LogInfo("ResetPasswordModel", $"Validating OTP for user: {userId}");

                // Validate OTP
                bool isValid = await _otpService.ValidateOtpAsync(userId.Value, VerificationCode);
                _logger.LogInfo("ResetPasswordModel", $"OTP validation result: {isValid}");

                if (isValid)
                {
                    _logger.LogInfo("ResetPasswordModel", "OTP validation successful");
                    // Mark OTP as used to prevent reuse
                    _otpService.MarkOtpAsUsed(userId.Value, VerificationCode);

                    // Store verification in TempData for password reset stage
                    TempData["VerifiedForReset"] = true;

                    // Show password reset form
                    ShowEmailForm = false;
                    ShowVerificationForm = false;
                    ShowResetForm = true;
                    IsForgotPasswordFlow = true;
                    StatusMessage = "Verification successful. Please enter your new password.";
                    return Page();
                }
                else
                {
                    _logger.LogInfo("ResetPasswordModel", "Invalid OTP provided");
                    ModelState.AddModelError("VerificationCode", "Invalid or expired verification code");
                    ShowVerificationForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error verifying code", ex);
                ErrorMessage = "An error occurred during verification. Please try again.";
                ShowVerificationForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }
        }

        public async Task<IActionResult> OnPostResetPasswordAsync()
        {
            _logger.LogInfo("ResetPasswordModel", "Processing password reset");

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
                _logger.LogInfo("ResetPasswordModel", "Invalid model state for password reset");
                ShowResetForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }

            try
            {
                // Verify that user is verified for reset
                if (TempData["VerifiedForReset"] == null || !(bool)TempData["VerifiedForReset"])
                {
                    _logger.LogError("ResetPasswordModel", "User not verified for password reset");
                    ErrorMessage = "Verification required. Please start the password reset process again.";
                    ShowEmailForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }

                // Get email and user ID from TempData
                string email = TempData["ResetEmail"]?.ToString();
                int? userId = TempData["UserId"] as int?;

                if (string.IsNullOrEmpty(email) || !userId.HasValue)
                {
                    _logger.LogError("ResetPasswordModel", "Email or user ID not found in TempData");
                    ErrorMessage = "Session expired. Please start the password reset process again.";
                    ShowEmailForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }

                // Hash the new password
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
                _logger.LogInfo("ResetPasswordModel", "New password hashed");

                // Update password in database
                bool updated = await UpdateUserPasswordAsync(userId.Value, passwordHash);
                _logger.LogInfo("ResetPasswordModel", $"Password update result: {updated}");

                if (updated)
                {
                    _logger.LogInfo("ResetPasswordModel", "Password reset successful");
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
                    _logger.LogError("ResetPasswordModel", "Failed to update password");
                    ErrorMessage = "Failed to update password. Please try again.";
                    ShowResetForm = true;
                    IsForgotPasswordFlow = true;
                    return Page();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error resetting password", ex);
                ErrorMessage = "An error occurred while resetting your password. Please try again.";
                ShowResetForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }
        }

        public IActionResult OnPostResendCodeAsync()
        {
            _logger.LogInfo("ResetPasswordModel", "Resending verification code");

            string email = TempData["ResetEmail"]?.ToString();
            int? userId = TempData["UserId"] as int?;

            if (string.IsNullOrEmpty(email) || !userId.HasValue)
            {
                _logger.LogError("ResetPasswordModel", "Email or user ID not found in TempData for resend");
                ErrorMessage = "Session expired. Please start the password reset process again.";
                ShowEmailForm = true;
                IsForgotPasswordFlow = true;
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
                _logger.LogInfo("ResetPasswordModel", $"Generated new OTP for resend: {otp}");
                _otpService.SaveOtp(userId.Value, otp);

                // Send email with OTP
                bool emailSent = _emailService.SendOtpEmail(email, otp);
                _logger.LogInfo("ResetPasswordModel", $"Resend email result: {emailSent}");

                if (emailSent)
                {
                    StatusMessage = "A new verification code has been sent to your email address.";
                }
                else
                {
                    ErrorMessage = "Failed to send verification email. Please try again.";
                }

                ShowVerificationForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error resending verification code", ex);
                ErrorMessage = "An error occurred while resending the code. Please try again.";
                ShowVerificationForm = true;
                IsForgotPasswordFlow = true;
                return Page();
            }
        }

        private async Task<bool> UserEmailExistsAsync(string email)
        {
            _logger.LogInfo("ResetPasswordModel", $"Checking if email exists: {email}");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT COUNT(*) FROM AppUsers WHERE EMAIL = @Email", connection);
                    command.Parameters.AddWithValue("@Email", email);

                    var exists = (int)await command.ExecuteScalarAsync() > 0;
                    _logger.LogInfo("ResetPasswordModel", $"Email exists check result: {exists}");
                    return exists;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", "Error checking email existence", ex);
                throw;
            }
        }

        private async Task<int> GetUserIdFromEmailAsync(string email)
        {
            _logger.LogInfo("ResetPasswordModel", $"Getting user ID for email: {email}");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT USER_ID FROM AppUsers WHERE EMAIL = @Email", connection);
                    command.Parameters.AddWithValue("@Email", email);

                    var result = await command.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                    {
                        throw new Exception($"No user found with email: {email}");
                    }

                    int userId = Convert.ToInt32(result);
                    _logger.LogInfo("ResetPasswordModel", $"Found user ID: {userId}");
                    return userId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", $"Error getting user ID for email: {email}", ex);
                throw;
            }
        }

        private async Task<bool> VerifyCurrentPasswordAsync(int userId, string password)
        {
            _logger.LogInfo("ResetPasswordModel", $"Verifying current password for user ID: {userId}");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT PASSWORD_HASH FROM AppUsers WHERE USER_ID = @UserId", connection);
                    command.Parameters.AddWithValue("@UserId", userId);

                    var passwordHash = await command.ExecuteScalarAsync() as string;

                    if (string.IsNullOrEmpty(passwordHash))
                    {
                        _logger.LogError("ResetPasswordModel", $"No password hash found for user ID: {userId}");
                        return false;
                    }

                    bool verified = BCrypt.Net.BCrypt.Verify(password, passwordHash);
                    _logger.LogInfo("ResetPasswordModel", $"Password verification result: {verified}");
                    return verified;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", $"Error verifying current password for user ID: {userId}", ex);
                throw;
            }
        }

        private async Task<bool> UpdateUserPasswordAsync(int userId, string passwordHash)
        {
            _logger.LogInfo("ResetPasswordModel", $"Updating password for user ID: {userId}");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "UPDATE AppUsers SET PASSWORD_HASH = @PasswordHash WHERE USER_ID = @UserId", connection);
                    command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                    command.Parameters.AddWithValue("@UserId", userId);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    bool success = rowsAffected > 0;
                    _logger.LogInfo("ResetPasswordModel", $"Password update rows affected: {rowsAffected}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ResetPasswordModel", $"Error updating password for user ID: {userId}", ex);
                throw;
            }
        }
    }
}