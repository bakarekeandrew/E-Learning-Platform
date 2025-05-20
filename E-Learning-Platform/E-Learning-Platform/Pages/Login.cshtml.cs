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

