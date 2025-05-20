using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using E_Learning_Platform.Pages.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages
{
    public class LoginModel : PageModel
    {
        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        private string ConnectionString => "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                                         "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                                         "Integrated Security=True;" +
                                         "TrustServerCertificate=True";

        private readonly OtpService _otpService;
        private readonly EmailService _emailService;
        private readonly IUserService _userService;
        private readonly LoggingService _logger;
        private readonly IConfiguration _configuration;

        public string ErrorMessage { get; set; } = string.Empty;
        public string SuccessMessage { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;

        public LoginModel(
            IUserService userService,
            LoggingService logger,
            OtpService otpService,
            EmailService emailService,
            IConfiguration configuration)
        {
            _userService = userService;
            _logger = logger;
            _otpService = otpService;
            _emailService = emailService;
            _configuration = configuration;
        }

        public void OnGet(string returnUrl = null, string error = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
            if (!string.IsNullOrEmpty(error))
            {
                ErrorMessage = "Authentication failed. Please try again.";
                _logger.LogError("Login", $"Authentication error: {error}");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }
            try
            {
                using var connection = new SqlConnection(ConnectionString);
                var user = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT U.USER_ID, U.FULL_NAME, U.PASSWORD_HASH, U.MFA_ENABLED, R.ROLE_NAME 
                      FROM USERS U 
                      JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID 
                      WHERE U.EMAIL = @Email",
                    new { Input.Email });

                if (user != null && BCrypt.Net.BCrypt.Verify(Input.Password, user.PASSWORD_HASH))
                {
                    // Check if MFA is enabled for this user
                    if (user.MFA_ENABLED)
                    {
                        // Generate and send OTP                        
                        var otp = _otpService.GenerateOtp();
                        _otpService.SaveOtp(user.USER_ID, otp);
                        _emailService.SendOtpEmail(Input.Email, otp);

                        // Store user info in TempData for MFA verification
                        TempData["PendingUserId"] = user.USER_ID;
                        TempData["PendingEmail"] = Input.Email;
                        TempData["PendingUserRole"] = user.ROLE_NAME;
                        TempData["PendingUserName"] = user.FULL_NAME;
                        TempData["RememberMe"] = Input.RememberMe;

                        // Redirect to MFA verification page
                        return RedirectToPage("/MfaVerification");
                    }
                    else
                    {
                        // No MFA, proceed with regular login
                        // Set session variables using the base Set method with byte arrays
                        HttpContext.Session.Set("UserId", BitConverter.GetBytes(user.USER_ID));
                        HttpContext.Session.Set("UserRole", System.Text.Encoding.UTF8.GetBytes(user.ROLE_NAME));
                        HttpContext.Session.Set("UserName", System.Text.Encoding.UTF8.GetBytes(user.FULL_NAME));

                        // Create claims
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, user.USER_ID.ToString()),
                            new Claim(ClaimTypes.Name, user.FULL_NAME),
                            new Claim(ClaimTypes.Email, Input.Email),
                            new Claim(ClaimTypes.Role, user.ROLE_NAME),
                            new Claim("UserId", user.USER_ID.ToString())
                        };

                        var authProperties = new AuthenticationProperties
                        {
                            IsPersistent = Input.RememberMe,
                            ExpiresUtc = Input.RememberMe
                                ? DateTimeOffset.UtcNow.AddDays(30)
                                : DateTimeOffset.UtcNow.AddHours(1)
                        };

                        await HttpContext.SignInAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                            authProperties);

                        return RedirectToPage(user.ROLE_NAME switch
                        {
                            "INSTRUCTOR" => "/Instructor/Dashboard",
                            "ADMIN" => "/AdminDashboard",
                            "STUDENT" => "/Student/Dashboard",
                            _ => "/Login"
                        });
                    }
                }

                ErrorMessage = "Invalid login email or password";
                return Page();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Login error: {ex.Message}";
                return Page();
            }
        }

        public IActionResult OnPostChallengeGoogleAsync()
        {
            // Request a redirect to the external login provider.
            var provider = "Google";
            var redirectUrl = Url.Page("/Login", pageHandler: "Callback"); // Callback handler on this page
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
        {
            returnUrl ??= Url.Content("~/");
            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var info = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme); // Trying to get external login info
            // A more robust way is often via SignInManager if using ASP.NET Core Identity, or directly checking specific external cookie
            // For Razor Pages without full Identity, we might need to inspect specific claims from an external cookie if HttpContext.GetExternalLoginInfoAsync() isn't readily available.
            // Let's assume for now the external login info can be retrieved like this or adjust if needed.

            // TEMPORARY: Attempt to get external login info directly. This part is tricky without full ASP.NET Core Identity's SignInManager.
            // We are looking for claims populated by the Google handler.
            var externalLoginInfo = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme); // Standard external cookie scheme
            if (externalLoginInfo?.Succeeded != true)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login");
            }

            var email = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Email);
            var fullName = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Name) ?? externalLoginInfo.Principal.FindFirstValue("name"); // Google might use 'name'
            var providerDisplayName = externalLoginInfo.Principal.Identity?.AuthenticationType; // Corrected line

            if (email == null)
            {
                ErrorMessage = "Email claim not received from Google. Please ensure your Google app requests the email scope.";
                return RedirectToPage("./Login");
            }

            // Check if the user already exists in your local database
            using var connection = new SqlConnection(ConnectionString);
            var user = await connection.QueryFirstOrDefaultAsync<
                (int UserId, string FullName, string PasswordHash, bool MfaEnabled, string RoleName) // Tuple for result
                >(
                "SELECT U.USER_ID, U.FULL_NAME, U.PASSWORD_HASH, U.MFA_ENABLED, R.ROLE_NAME FROM USERS U JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID WHERE U.EMAIL = @Email",
                new { Email = email });

            if (user.UserId != 0) // User exists
            {
                // User exists, sign them in
            }
            else
            {
                // User does not exist, create a new user account
                // For simplicity, assign a default role (e.g., STUDENT)
                // You might want to redirect to a registration completion page if more info is needed

                var studentRole = await connection.QuerySingleOrDefaultAsync<RoleDto>("SELECT ROLE_ID as RoleId, ROLE_NAME as RoleName FROM ROLES WHERE ROLE_NAME = 'STUDENT'");
                if (studentRole == null)
                {
                    ErrorMessage = "Default role 'STUDENT' not found. Cannot create new user.";
                    return RedirectToPage("./Login");
                }

                var newUser = new
                {
                    FullName = fullName ?? email, // Use email if full name is not provided
                    Email = email,
                    PasswordHash = "EXT_LOGIN_NO_PWD_" + Guid.NewGuid().ToString(), // No local password for external login, or generate one
                    RoleId = studentRole.RoleId,
                    DateRegistered = DateTime.UtcNow,
                    IsActive = true, // Activate user by default
                    MfaEnabled = false // MFA typically not enforced initially for external logins this way
                };

                var insertSql = @"
                    INSERT INTO USERS (FULL_NAME, EMAIL, PASSWORD_HASH, ROLE_ID, DATE_REGISTERED, IS_ACTIVE, MFA_ENABLED)
                    VALUES (@FullName, @Email, @PasswordHash, @RoleId, @DateRegistered, @IsActive, @MfaEnabled);
                    SELECT CAST(SCOPE_IDENTITY() as int);
                ";
                var newUserId = await connection.ExecuteScalarAsync<int>(insertSql, newUser);
                user = (newUserId, newUser.FullName, newUser.PasswordHash, newUser.MfaEnabled, studentRole.RoleName);
            }




