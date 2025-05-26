using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages
{
    public class TestNotificationsModel : PageModel
    {
        private readonly INotificationService _notificationService;

        [BindProperty]
        public string Title { get; set; }

        [BindProperty]
        public string Message { get; set; }

        [BindProperty]
        public string Type { get; set; }

        public TestNotificationsModel(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToPage("/Login");
            }

            await _notificationService.CreateNotificationAsync(userId.Value, Title, Message, Type);
            return RedirectToPage("/TestNotifications");
        }
    }
} 