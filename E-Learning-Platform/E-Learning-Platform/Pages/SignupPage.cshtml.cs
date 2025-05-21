using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using Org.BouncyCastle.Crypto.Generators;
using E_Learning_Platform.Pages.Services;
using Microsoft.AspNetCore.Authorization;

namespace E_Learning_Platform.Pages
{
    [AllowAnonymous]
    public class SignupPageModel : PageModel
    {
        private readonly LoggingService _logger = new LoggingService();

        public class InputModel
        {
            [Required]
            [Display(Name = "Full Name")]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [StringLength(100, MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "Passwords don't match")]
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        [BindProperty]
        [Display(Name = "Verification Code")]
        public string VerificationCode { get; set; } = string.Empty;

        // No longer need to keep a list of roles as we'll always assign Student role
        public bool ShowVerificationForm { get; set; }
        public string UserEmail { get; set; }
        public string StatusMessage { get; set; }
        public string ErrorMessage { get; set; }

        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                       "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                       "Integrated Security=True;" +
                                       "TrustServerCertificate=True";

        private readonly OtpService _otpService;
        private readonly EmailService _emailService;

        public SignupPageModel()
        {
            _logger.LogInfo("SignupPageModel", "Initializing SignupPageModel");
            _otpService = new OtpService(ConnectionString);
            _emailService = new EmailService();
        }

        public async Task OnGetAsync()
        {
            _logger.LogInfo("SignupPageModel", "Loading signup page");
            // No need to load roles anymore
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInfo("SignupPageModel", "Signup form submitted");

            // Only validate the Input model when submitting the signup form
            if (!ModelState.IsValid)
            {
                // Filter out any validation errors for VerificationCode as it's not required at this stage
                foreach (var key in ModelState.Keys.Where(k => k.StartsWith("VerificationCode")).ToList())
                {
                    ModelState.Remove(key);
                }

                // Check if the InputModel is still invalid
                if (!TryValidateModel(Input))
                {
                    _logger.LogInfo("SignupPageModel", "Input model validation failed");
                    return Page();
                }
            }

            try
            {
                _logger.LogInfo("SignupPageModel", $"Checking if email exists: {Input.Email}");
                if (await EmailExistsAsync(Input.Email))
                {
                    _logger.LogInfo("SignupPageModel", $"Email already exists: {Input.Email}");
                    ModelState.AddModelError("Input.Email", "Email already registered");
                    return Page();
                }

                // Generate OTP and store temporary user data
                var otp = _otpService.GenerateOtp();
                _logger.LogInfo("SignupPageModel", $"Generated OTP: {otp}");

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(Input.Password);
                _logger.LogInfo("SignupPageModel", "Password hashed successfully");

                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInfo("SignupPageModel", "Database connection opened");

                    // Query to get Student role ID
                    var studentRoleQuery = new SqlCommand(
                        "SELECT ROLE_ID FROM ROLES WHERE ROLE_NAME = 'Student'", connection);
                    var studentRoleId = (int)await studentRoleQuery.ExecuteScalarAsync();
                    _logger.LogInfo("SignupPageModel", $"Found Student role ID: {studentRoleId}");

                    var command = new SqlCommand(
                        "INSERT INTO TEMP_USERS (FULL_NAME, ROLE_ID, EMAIL, PASSWORD_HASH, OTP_CODE, OTP_EXPIRY) " +
                        "VALUES (@FullName, @RoleId, @Email, @PasswordHash, @OtpCode, DATEADD(MINUTE, 10, GETDATE())); " +
                        "SELECT SCOPE_IDENTITY();", connection);

                    command.Parameters.AddWithValue("@FullName", Input.FullName);
                    command.Parameters.AddWithValue("@RoleId", studentRoleId); // Automatically assign Student role
                    command.Parameters.AddWithValue("@Email", Input.Email);
                    command.Parameters.AddWithValue("@PasswordHash", passwordHash);
                    command.Parameters.AddWithValue("@OtpCode", otp);

                    var tempUserId = Convert.ToInt32(await command.ExecuteScalarAsync());
                    _logger.LogInfo("SignupPageModel", $"Temporary user created with ID: {tempUserId}");

                    // Store tempUserId in session for verification
                    HttpContext.Session.SetInt32("TempUserId", tempUserId);
                }

                // Send OTP email
                _logger.LogInfo("SignupPageModel", $"Sending OTP email to: {Input.Email}");
                if (_emailService.SendOtpEmail(Input.Email, otp))
                {
                    _logger.LogInfo("SignupPageModel", "OTP email sent successfully");
                    ShowVerificationForm = true;
                    UserEmail = Input.Email;
                    StatusMessage = $"We've sent a verification code to {Input.Email}. Please check your inbox.";
                }
                else
                {
                    _logger.LogError("SignupPageModel", "Failed to send OTP email");
                    ErrorMessage = "Failed to send verification email. Please try again.";
                }

                return Page();
            }
            catch (SqlException ex)
            {
                _logger.LogError("SignupPageModel", "Database error during signup", ex);
                ErrorMessage = "A database error occurred. Please try again.";
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("SignupPageModel", "Unexpected error during signup", ex);
                ErrorMessage = "An unexpected error occurred. Please try again.";
                return Page();
            }
        }

        public async Task<IActionResult> OnPostVerifyAsync()
        {
            _logger.LogInfo("SignupPageModel", "OTP verification submitted");

            // Only validate VerificationCode when it's needed
            if (string.IsNullOrEmpty(VerificationCode))
            {
                _logger.LogInfo("SignupPageModel", "Verification code is empty");
                ModelState.AddModelError("VerificationCode", "Please enter the verification code");
                ShowVerificationForm = true;
                return Page();
            }

            try
            {
                var tempUserId = HttpContext.Session.GetInt32("TempUserId");
                if (tempUserId == null)
                {
                    _logger.LogError("SignupPageModel", "TempUserId not found in session");
                    ErrorMessage = "Session expired. Please start the signup process again.";
                    return RedirectToPage();
                }

                _logger.LogInfo("SignupPageModel", $"Verifying OTP for temp user: {tempUserId}");
                if (!await _otpService.ValidateTempUserOtpAsync(tempUserId.Value, VerificationCode))
                {
                    _logger.LogInfo("SignupPageModel", "Invalid OTP provided");
                    ModelState.AddModelError("VerificationCode", "Invalid or expired verification code");
                    ShowVerificationForm = true;
                    return Page();
                }

                // Move from TEMP_USERS to USERS
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInfo("SignupPageModel", "Database connection opened for verification");

                    var command = new SqlCommand(
                        "INSERT INTO USERS (FULL_NAME, ROLE_ID, EMAIL, PASSWORD_HASH, DATE_REGISTERED) " +
                        "SELECT FULL_NAME, ROLE_ID, EMAIL, PASSWORD_HASH, GETDATE() " +
                        "FROM TEMP_USERS WHERE TEMP_USER_ID = @TempUserId; " +
                        "DELETE FROM TEMP_USERS WHERE TEMP_USER_ID = @TempUserId;", connection);

                    command.Parameters.AddWithValue("@TempUserId", tempUserId.Value);
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInfo("SignupPageModel", $"User successfully moved from temp to permanent table: {tempUserId}");
                }

                // Clear session
                HttpContext.Session.Remove("TempUserId");

                _logger.LogInfo("SignupPageModel", "Signup process completed successfully");
                return RedirectToPage("/Login", new { signupSuccess = true });
            }
            catch (SqlException ex)
            {
                _logger.LogError("SignupPageModel", "Database error during OTP verification", ex);
                ErrorMessage = "A database error occurred. Please try again.";
                ShowVerificationForm = true;
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError("SignupPageModel", "Unexpected error during OTP verification", ex);
                ErrorMessage = "An unexpected error occurred. Please try again.";
                ShowVerificationForm = true;
                return Page();
            }
        }

        private async Task<bool> EmailExistsAsync(string email)
        {
            _logger.LogInfo("SignupPageModel", $"Checking if email exists: {email}");
            try
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    await connection.OpenAsync();
                    var command = new SqlCommand(
                        "SELECT COUNT(*) FROM USERS WHERE EMAIL = @Email", connection);
                    command.Parameters.AddWithValue("@Email", email);

                    var exists = (int)await command.ExecuteScalarAsync() > 0;
                    _logger.LogInfo("SignupPageModel", $"Email exists check result: {exists}");
                    return exists;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("SignupPageModel", "Error checking email existence", ex);
                throw;
            }
        }

        // Role class removed as it's no longer needed
    }
}