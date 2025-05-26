using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using E_Learning_Platform.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Dapper;

namespace E_Learning_Platform.Pages
{
    [Authorize]
    public class ProfileModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILoggingService _logger;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;

        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool MfaEnabled { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SuccessMessage { get; set; } = string.Empty;

        [BindProperty]
        public bool EnableMfa { get; set; }

        public ProfileModel(
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
            _logger.LogInfo("ProfileModel", "Loading user profile");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                _logger.LogError("ProfileModel", "User ID not found in claims");
                ErrorMessage = "Unable to identify the current user. Please try logging in again.";
                return Page();
            }

            try
            {
                // Retrieve user information
                await LoadUserProfileAsync(userId);

                // Set the current MFA status in the form
                EnableMfa = MfaEnabled;

                // Check if we have a success message from MFA toggle or other operations
                if (TempData["SuccessMessage"] != null)
                {
                    SuccessMessage = TempData["SuccessMessage"].ToString();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("ProfileModel", "Error loading user profile", ex);
                ErrorMessage = "An error occurred while loading your profile. Please try again.";
                return Page();
            }
        }

        public async Task<IActionResult> OnPostToggleMfaAsync()
        {
            _logger.LogInfo("ProfileModel", $"Toggling MFA status to: {EnableMfa}");

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            {
                _logger.LogError("ProfileModel", "User ID not found in claims");
                ErrorMessage = "Unable to identify the current user. Please try logging in again.";
                return Page();
            }

            try
            {
                // Retrieve user information first to get current MFA status
                await LoadUserProfileAsync(userId);

                // If enabling MFA and it's currently disabled
                if (EnableMfa && !MfaEnabled)
                {
                    _logger.LogInfo("ProfileModel", "Initiating MFA enablement process");

                    // Store user info in TempData for MFA setup
                    TempData["PendingMfaUserId"] = userId;
                    TempData["CurrentMfaStatus"] = MfaEnabled;
                    TempData["RequestedMfaStatus"] = EnableMfa;

                    // Generate and send OTP for verification
                    var otp = _otpService.GenerateOtp();
                    _otpService.SaveOtp(userId, otp);
                    _emailService.SendOtpEmail(Email, otp);

                    // Redirect to MFA setup page
                    return RedirectToPage("/Account/MfaSetup");
                }
                // If disabling MFA and it's currently enabled
                else if (!EnableMfa && MfaEnabled)
                {
                    // Update the user's MFA status directly
                    bool updated = await UpdateMfaStatusAsync(userId, false);

                    if (updated)
                    {
                        TempData["SuccessMessage"] = "Two-factor authentication has been disabled.";
                    }
                    else
                    {
                        ErrorMessage = "Failed to update two-factor authentication status.";
                    }
                }

                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError("ProfileModel", "Error toggling MFA status", ex);
                ErrorMessage = "An error occurred while updating your settings. Please try again.";
                await LoadUserProfileAsync(userId); // Reload profile data
                return Page();
            }
        }

        private async Task LoadUserProfileAsync(int userId)
        {
            _logger.LogInfo("ProfileModel", $"Loading profile data for user ID: {userId}");

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(@"
                        SELECT u.FULL_NAME, u.EMAIL, u.MFA_ENABLED, r.ROLE_NAME 
                        FROM AppUsers u
                        JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                        WHERE u.USER_ID = @UserId", connection);

                    command.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            FullName = reader["FULL_NAME"].ToString();
                            Email = reader["EMAIL"].ToString();
                            MfaEnabled = Convert.ToBoolean(reader["MFA_ENABLED"]);
                            Role = reader["ROLE_NAME"].ToString();

                            _logger.LogInfo("ProfileModel", $"Loaded profile: {FullName}, {Email}, Role: {Role}, MFA: {MfaEnabled}");
                        }
                        else
                        {
                            _logger.LogError("ProfileModel", $"No user found with ID: {userId}");
                            throw new Exception("User not found");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ProfileModel", $"Error loading profile for user ID: {userId}", ex);
                throw;
            }
        }

        private async Task<bool> UpdateMfaStatusAsync(int userId, bool enableMfa)
        {
            _logger.LogInfo("ProfileModel", $"Updating MFA status to {enableMfa} for user ID: {userId}");

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    var updateCommand = new SqlCommand(@"
                        UPDATE AppUsers 
                        SET FULL_NAME = @FullName, 
                            EMAIL = @Email 
                        WHERE USER_ID = @UserId", connection);

                    updateCommand.Parameters.AddWithValue("@FullName", FullName);
                    updateCommand.Parameters.AddWithValue("@Email", Email);
                    updateCommand.Parameters.AddWithValue("@UserId", userId);

                    int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                    bool success = rowsAffected > 0;

                    _logger.LogInfo("ProfileModel", $"MFA update rows affected: {rowsAffected}");
                    return success;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("ProfileModel", $"Error updating MFA status for user ID: {userId}", ex);
                throw;
            }
        }
    }
}