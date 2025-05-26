using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using E_Learning_Platform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace E_Learning_Platform.Pages
{
    [AllowAnonymous]
    public class SignupPageModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILoggingService _logger;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly IRoleService _roleService;

        public SignupPageModel(
            IConfiguration configuration,
            ILoggingService logger,
            IOtpService otpService,
            IEmailService emailService,
            IRoleService roleService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _otpService = otpService;
            _emailService = emailService;
            _roleService = roleService;
        }

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

        public bool ShowVerificationForm { get; set; }
        public string UserEmail { get; set; }
        public string StatusMessage { get; set; }
        public string ErrorMessage { get; set; }

        public void OnGet()
        {
            _logger.LogInfo("SignupPageModel", "Loading signup page");
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInfo("SignupPageModel", "Signup form submitted");

            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys.Where(k => k.StartsWith("VerificationCode")).ToList())
                {
                    ModelState.Remove(key);
                }

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

                var otp = _otpService.GenerateOtp();
                _logger.LogInfo("SignupPageModel", $"Generated OTP: {otp}");

                // Generate password hash with controlled cost
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(Input.Password, 12);
                _logger.LogInfo("SignupPageModel", $"Password hashed successfully. Hash length: {passwordHash.Length}");

                // Validate hash length immediately
                if (passwordHash.Length > 255)
                {
                    _logger.LogError("SignupPageModel", $"BCrypt hash too long: {passwordHash.Length} characters");
                    ErrorMessage = "Password processing error. Please try again or contact support.";
                    return Page();
                }

                var studentRoleId = await _roleService.GetDefaultRoleIdAsync();
                _logger.LogInfo("SignupPageModel", $"Found Student role ID: {studentRoleId}");

                // Validate role ID before proceeding
                if (studentRoleId <= 0)
                {
                    _logger.LogError("SignupPageModel", $"Invalid role ID returned from service: {studentRoleId}");
                    ErrorMessage = "System configuration error: Unable to assign user role. Please contact support.";
                    return Page();
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();
                    _logger.LogInfo("SignupPageModel", "Database connection opened");

                    var command = new SqlCommand(
                        "INSERT INTO TEMP_USERS (FULL_NAME, EMAIL, PASSWORD_HASH, ROLE_ID, OTP_CODE, OTP_EXPIRY, CREATED_AT) " +
                        "VALUES (@FullName, @Email, @PasswordHash, @RoleId, @OtpCode, DATEADD(MINUTE, 10, GETDATE()), GETDATE()); " +
                        "SELECT CAST(SCOPE_IDENTITY() as int);", connection);

                    command.Parameters.AddWithValue("@FullName", Input.FullName ?? string.Empty);
                    command.Parameters.AddWithValue("@Email", Input.Email ?? string.Empty);
                    command.Parameters.AddWithValue("@PasswordHash", passwordHash ?? string.Empty);
                    command.Parameters.AddWithValue("@RoleId", studentRoleId);
                    command.Parameters.AddWithValue("@OtpCode", otp ?? string.Empty);

                    var tempUserId = Convert.ToInt32(await command.ExecuteScalarAsync());
                    _logger.LogInfo("SignupPageModel", $"Temporary user created with ID: {tempUserId}");

                    HttpContext.Session.SetInt32("TempUserId", tempUserId);
                }

                _logger.LogInfo("SignupPageModel", $"Sending OTP email to: {Input.Email}");
                if (await _emailService.SendOtpEmailAsync(Input.Email, otp)) 
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
                _logger.LogError("SignupPageModel", $"Database error during signup. Error Number: {ex.Number}, Message: {ex.Message}", ex);
                ErrorMessage = $"A database error occurred: {ex.Message}";
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
            _logger.LogInfo("SignupPageModel", "Starting OTP verification process");

            if (string.IsNullOrEmpty(VerificationCode))
            {
                ModelState.AddModelError("VerificationCode", "Please enter the verification code");
                ShowVerificationForm = true;
                return Page();
            }

            try
            {
                var tempUserId = HttpContext.Session.GetInt32("TempUserId");
                if (tempUserId == null)
                {
                    ErrorMessage = "Session expired. Please start the signup process again.";
                    return RedirectToPage();
                }

                _logger.LogInfo("SignupPageModel", $"Validating OTP for tempUserId: {tempUserId}");
                var isValidOtp = await _otpService.ValidateTempUserOtpAsync(tempUserId.Value, VerificationCode);
                if (!isValidOtp)
                {
                    ModelState.AddModelError("VerificationCode", "Invalid or expired verification code");
                    ShowVerificationForm = true;
                    return Page();
                }

                using (var connection = new SqlConnection(_connectionString))
                {
                    try
                    {
                        await connection.OpenAsync();
                        _logger.LogInfo("SignupPageModel", "Database connection opened successfully");
                    }
                    catch (SqlException ex)
                    {
                        _logger.LogError("SignupPageModel", $"Failed to open database connection: {ex.Message}, Error Number: {ex.Number}");
                        ErrorMessage = "Unable to connect to the database. Please try again.";
                        ShowVerificationForm = true;
                        return Page();
                    }

                    using SqlTransaction transaction = await connection.BeginTransactionAsync() as SqlTransaction;
                    _logger.LogInfo("SignupPageModel", "Transaction started");

                    try
                    {
                        // First verify temp user still exists
                        var verifyCommand = new SqlCommand(
                            @"SELECT COUNT(1) FROM TEMP_USERS WHERE TEMP_USER_ID = @TempUserId",
                            connection, transaction);
                        verifyCommand.Parameters.AddWithValue("@TempUserId", tempUserId.Value);

                        var exists = (int)await verifyCommand.ExecuteScalarAsync() > 0;
                        if (!exists)
                        {
                            _logger.LogError("SignupPageModel", $"Temp user {tempUserId} not found in database");
                            throw new Exception("Verification session expired. Please start over.");
                        }

                        // Get temp user details
                        var getTempUserCommand = new SqlCommand(
                            @"SELECT EMAIL, FULL_NAME, USERNAME, PASSWORD_HASH FROM TEMP_USERS WHERE TEMP_USER_ID = @TempUserId",
                            connection, transaction);
                        getTempUserCommand.Parameters.AddWithValue("@TempUserId", tempUserId.Value);

                        using var reader = await getTempUserCommand.ExecuteReaderAsync();
                        if (!await reader.ReadAsync())
                        {
                            throw new Exception("User data not found. Please start over.");
                        }

                        var email = reader.GetString(0);
                        var fullName = reader.GetString(1);
                        var username = reader.GetString(2);
                        var passwordHash = reader.GetString(3);
                        var normalizedUsername = username.ToUpper();

                        reader.Close();

                        // Get default role ID
                        var roleId = await _roleService.GetDefaultRoleIdAsync();

                        // Insert the user
                        var insertCommand = new SqlCommand(
                            @"INSERT INTO USERS (FULL_NAME, EMAIL, PASSWORD_HASH, USERNAME, NORMALIZED_USERNAME, ROLE_ID, CREATED_AT, IS_ACTIVE)
                            OUTPUT INSERTED.USER_ID
                            VALUES (@FullName, @Email, @PasswordHash, @Username, @NormalizedUsername, @RoleId, GETUTCDATE(), 1)",
                            connection, transaction);

                        insertCommand.Parameters.AddWithValue("@FullName", fullName);
                        insertCommand.Parameters.AddWithValue("@Email", email);
                        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
                        insertCommand.Parameters.AddWithValue("@Username", username);
                        insertCommand.Parameters.AddWithValue("@NormalizedUsername", normalizedUsername);
                        insertCommand.Parameters.AddWithValue("@RoleId", roleId);

                        int userId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());
                        _logger.LogInfo("SignupPageModel", $"User created successfully with ID: {userId}");

                        // Delete temp user
                        var deleteTempUserCommand = new SqlCommand(
                            "DELETE FROM TEMP_USERS WHERE TEMP_USER_ID = @TempUserId",
                            connection, transaction);
                        deleteTempUserCommand.Parameters.AddWithValue("@TempUserId", tempUserId.Value);
                        await deleteTempUserCommand.ExecuteNonQueryAsync();

                        await transaction.CommitAsync();
                        _logger.LogInfo("SignupPageModel", "Transaction committed successfully");

                        // Clear session
                        HttpContext.Session.Clear();

                        TempData["SuccessMessage"] = "Your account has been created successfully. Please log in.";
                        return RedirectToPage("/Login");
                    }
                    catch (SqlException ex)
                    {
                        if (transaction != null)
                        {
                            await transaction.RollbackAsync();
                            _logger.LogInfo("SignupPageModel", "Transaction rolled back");
                        }

                        _logger.LogError("SignupPageModel", $"SQL error during user creation: {ex.Message}");
                        ErrorMessage = GetUserFriendlyDbError(ex);
                        ShowVerificationForm = true;
                        return Page();
                    }
                    catch (Exception ex)
                    {
                        if (transaction != null)
                        {
                            await transaction.RollbackAsync();
                            _logger.LogInfo("SignupPageModel", "Transaction rolled back");
                        }

                        _logger.LogError("SignupPageModel", $"Error during user creation: {ex.Message}");
                        ErrorMessage = "An error occurred while creating your account. Please try again.";
                        ShowVerificationForm = true;
                        return Page();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("SignupPageModel", $"Error during verification: {ex.Message}");
                ErrorMessage = "An error occurred during verification. Please try again.";
                ShowVerificationForm = true;
                return Page();
            }
        }

        private string GetUserFriendlyDbError(SqlException ex)
        {
            switch (ex.Number)
            {
                case 515:  // Cannot insert null value
                    return "Required user information is missing. Please try the signup process again.";
                case 2627: // Unique constraint error
                    return "This email address is already registered.";
                case 547:  // Foreign key violation
                    return "Invalid role assignment. Please try again.";
                case 201:  // Procedure or parameter missing
                case 207:  // Invalid column name
                    return "There was a configuration error. Please contact support.";
                case 4060: // Database offline
                case 18456: // Login failed
                    return "Database connection error. Please try again later.";
                default:
                    return $"A database error occurred (Error {ex.Number}). Please try again.";
            }
        }

        private async Task<bool> EmailExistsAsync(string email)
        {
            _logger.LogInfo("SignupPageModel", $"Checking if email exists: {email}");
            try
            {
                using (var connection = new SqlConnection(_connectionString))
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
    }
}