using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Dapper;
using E_Learning_Platform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages
{
    public class LoginModel : PageModel
    {
        public class InputModel
        {
            [Required(ErrorMessage = "Email is required")]
            [EmailAddress(ErrorMessage = "Invalid email format")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Password is required")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        private readonly OtpService _otpService;
        private readonly EmailService _emailService;
        private readonly IUserService _userService;
        private readonly ILoggingService _logger;
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public string ErrorMessage { get; set; } = string.Empty;
        public string SuccessMessage { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;

        public LoginModel(
            IUserService userService,
            ILoggingService logger,
            IOtpService otpService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _otpService = (OtpService)otpService ?? throw new ArgumentNullException(nameof(otpService));
            _emailService = (EmailService)emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        public void OnGet(string returnUrl = null, string error = null, string signupSuccess = null)
        {
            // Clean and validate the returnUrl to prevent open redirect vulnerabilities
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                // If we're already on the login page, don't keep adding it to the return URL
                if (!returnUrl.Contains("/Login", StringComparison.OrdinalIgnoreCase))
                {
                    ReturnUrl = returnUrl;
                }
                else
                {
                    ReturnUrl = Url.Content("~/");
                }
            }
            else
            {
                ReturnUrl = Url.Content("~/");
            }
            
            if (!string.IsNullOrEmpty(error))
            {
                ErrorMessage = "Authentication failed. Please try again.";
                _logger.LogError("Login", $"Authentication error: {error}");
            }

            if (!string.IsNullOrEmpty(signupSuccess))
            {
                SuccessMessage = "Registration successful! Please log in with your credentials.";
            }

            if (TempData["ResetSuccess"] != null)
            {
                SuccessMessage = TempData["ResetSuccess"].ToString();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            _logger.LogInfo("Login", "Starting login process");
            
            if (!ModelState.IsValid)
            {
                var errors = string.Join(", ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                _logger.LogWarning("Login", "Invalid model state: {Errors}", errors);
                return Page();
            }

            try
            {
                _logger.LogInfo("Login", "Attempting login for email: {Email}", Input.Email);
                
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                
                // Get basic user information
                var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    @"SELECT 
                        U.USER_ID, 
                        U.FULL_NAME, 
                        U.PASSWORD_HASH, 
                        U.MFA_ENABLED, 
                        R.ROLE_NAME
                      FROM USERS U 
                      JOIN ROLES R ON U.ROLE_ID = R.ROLE_ID 
                      WHERE U.EMAIL = @Email",
                    new { Input.Email });

                if (user == null)
                {
                    _logger.LogWarning("Login", "User not found: {Email}", Input.Email);
                    ModelState.AddModelError(string.Empty, "Invalid email or password");
                    return Page();
                }

                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(Input.Password, user.PASSWORD_HASH);
                if (!isPasswordValid)
                {
                    _logger.LogWarning("Login", "Invalid password for user: {Email}", Input.Email);
                    ModelState.AddModelError(string.Empty, "Invalid email or password");
                    return Page();
                }

                if (user.MFA_ENABLED)
                {
                    _logger.LogInfo("Login", "MFA is enabled, generating OTP");
                    var otp = _otpService.GenerateOtp();
                    _otpService.SaveOtp(user.USER_ID, otp);
                    await _emailService.SendOtpEmailAsync(Input.Email, otp);

                    TempData["PendingUserId"] = user.USER_ID;
                    TempData["PendingEmail"] = Input.Email;
                    TempData["PendingUserRole"] = user.ROLE_NAME;
                    TempData["PendingUserName"] = user.FULL_NAME;
                    TempData["RememberMe"] = Input.RememberMe;
                    TempData["ReturnUrl"] = ReturnUrl;

                    return RedirectToPage("/MfaVerification");
                }

                await CompleteSignInAsync(
                    userId: user.USER_ID,
                    fullName: user.FULL_NAME,
                    email: Input.Email,
                    roleName: user.ROLE_NAME,
                    rememberMe: Input.RememberMe);

                // Determine landing page based on role
                var landingPage = user.ROLE_NAME switch
                {
                    "ADMIN" => "/AdminDashboard",
                    "INSTRUCTOR" => "/Instructor/Dashboard",
                    "STUDENT" => "/Student/Dashboard",
                    _ => "/HomePage"
                };

                if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
                {
                    return LocalRedirect(ReturnUrl);
                }

                _logger.LogInfo("Login", "Login successful. Redirecting to: {LandingPage}", landingPage);
                return LocalRedirect(landingPage);
            }
            catch (Exception ex)
            {
                _logger.LogError("Login", "Exception during login", ex);
                ModelState.AddModelError(string.Empty, "An error occurred during login. Please try again.");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostLogoutAsync(string returnUrl = null)
        {
            _logger.LogInfo("Login", "Starting logout process");
            
            // Clear the authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            
            // Clear the session
            HttpContext.Session.Clear();
            
            _logger.LogInfo("Login", "Logout completed successfully");
            
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            
            return RedirectToPage("/Login");
        }

        private async Task CompleteSignInAsync(int userId, string fullName, string email, string roleName, bool rememberMe)
        {
            try
            {
                _logger.LogInfo("Login", "Starting CompleteSignInAsync for user {UserId} ({Email})", userId, email);
                
                // Clear any existing authentication
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                HttpContext.Session.Clear();

                // Set session data
                HttpContext.Session.SetInt32("UserId", userId);
                HttpContext.Session.SetString("UserRole", roleName);
                HttpContext.Session.SetString("UserName", fullName);

                // Create basic claims
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, fullName),
                    new Claim(ClaimTypes.Email, email),
                    new Claim(ClaimTypes.Role, roleName),
                    new Claim("UserId", userId.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = rememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
                    AllowRefresh = true
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                _logger.LogInfo("Login", "Sign-in completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("Login", "Error in CompleteSignInAsync: {Error}\nStack trace: {StackTrace}", ex.Message, ex.StackTrace);
                throw;
            }
        }
    }
}