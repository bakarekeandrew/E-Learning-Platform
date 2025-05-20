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



