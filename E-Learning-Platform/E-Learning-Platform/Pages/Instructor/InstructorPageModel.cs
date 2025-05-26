using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using E_Learning_Platform.Pages.Instructor.Infrastructure;

namespace E_Learning_Platform.Pages.Instructor
{
    [Authorize(Roles = "INSTRUCTOR")]
    public abstract class InstructorPageModel : PageModel
    {
        protected readonly ILogger _logger;
        protected readonly string _connectionString;
        protected readonly IConfiguration _configuration;

        protected InstructorPageModel(ILogger logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        protected int? GetInstructorId()
        {
            try
            {
                // First try to get from claims
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userIdFromClaim))
                {
                    return userIdFromClaim;
                }

                // Then try to get from session
                var userIdFromSession = HttpContext.Session.GetInt32("UserId");
                if (userIdFromSession.HasValue)
                {
                    return userIdFromSession.Value;
                }

                _logger.LogWarning("No valid instructor ID found in claims or session");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving instructor ID");
                return null;
            }
        }

        protected async Task<bool> ValidateInstructor()
        {
            var instructorId = GetInstructorId();
            if (!instructorId.HasValue)
            {
                _logger.LogWarning("Instructor ID not found in session");
                return false;
            }
            
            // Additional validation to ensure it's a valid instructor
            if (!User.IsInRole("INSTRUCTOR"))
            {
                _logger.LogWarning("User is not in INSTRUCTOR role");
                return false;
            }
            
            return true;
        }

        protected async Task<IActionResult> ExecuteDbOperationAsync(Func<Task<IActionResult>> operation, string errorMessage)
        {
            try
            {
                if (!await ValidateInstructor())
                {
                    return RedirectToPage("/Login");
                }

                return await operation();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, errorMessage);
                ModelState.AddModelError("", errorMessage);
                return Page();
            }
        }

        protected void AddModelError(string message, Exception ex = null)
        {
            if (ex != null)
            {
                _logger.LogError(ex, message);
            }
            ModelState.AddModelError("", message);
        }
    }
} 