using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace E_Learning_Platform.Pages.Instructor.Courses
{
    //[Authorize(Roles = "INSTRUCTOR")]
    public class CreateModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(ILogger<CreateModel> logger)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
            _logger = logger;
        }

        [BindProperty]
        public CourseInput Course { get; set; } = new CourseInput();

        public SelectList Categories { get; set; }
        public SelectList Levels { get; set; }
        public string ErrorMessage { get; set; }

        public class CourseInput
        {
            [Required]
            [StringLength(100)]
            public string Title { get; set; }

            [Required]
            [StringLength(1000)]
            public string Description { get; set; }

            [Required]
            [Display(Name = "Category")]
            public int CategoryId { get; set; }

            [Required]
            [Display(Name = "Level")]
            public string Level { get; set; }

            [Display(Name = "Course Image")]
            public IFormFile ImageFile { get; set; }

            [Display(Name = "Publish this course")]
            public bool IsActive { get; set; }
        }

        public async Task OnGetAsync()
        {
            await LoadCategories();
            LoadLevels();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadCategories();
                LoadLevels();
                return Page();
            }

            // Get the current user's ID (instructor)
            // Replace the user ID retrieval code with this
            int? instructorId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                instructorId = BitConverter.ToInt32(userIdBytes, 0);
            }

            if (instructorId == null)
            {
                ModelState.AddModelError("", "Unable to identify instructor. Please login again.");
                await LoadCategories();
                LoadLevels();
                return Page();
            }
            try
            {
                var imagePath = await SaveImage();

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var courseId = await connection.ExecuteScalarAsync<int>(@"
                    INSERT INTO COURSES (
                        TITLE, 
                        DESCRIPTION, 
                        CREATED_BY, 
                        CREATION_DATE, 
                        IS_ACTIVE, 
                        THUMBNAIL_URL,
                        CATEGORY_ID,
                        REQUIREMENTS
                    )
                    OUTPUT INSERTED.COURSE_ID
                    VALUES (
                        @Title, 
                        @Description, 
                        @CreatedBy, 
                        GETDATE(), 
                        @IsActive, 
                        @ImageUrl,
                        @CategoryId,
                        @Level
                    )",
                    new
                    {
                        Course.Title,
                        Course.Description,
                        CreatedBy = instructorId,
                        Course.IsActive,
                        ImageUrl = imagePath,
                        Course.CategoryId,
                        Level = Course.Level
                    });

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating course");
                ErrorMessage = $"An error occurred while creating the course: {ex.Message}";
                await LoadCategories();
                LoadLevels();
                return Page();
            }
        }

        private async Task LoadCategories()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var categories = await connection.QueryAsync<Category>(
                    "SELECT CATEGORY_ID as CategoryId, NAME as Name FROM CATEGORIES WHERE IS_ACTIVE = 1 ORDER BY NAME");

                Categories = new SelectList(categories, "CategoryId", "Name");
            }
            catch (Exception ex)
            {
                // Log the error if needed
                Categories = new SelectList(Enumerable.Empty<Category>());
            }
        }

        private void LoadLevels()
        {
            Levels = new SelectList(new[] { "Beginner", "Intermediate", "Advanced" });
        }

        private async Task<string> SaveImage()
        {
            if (Course.ImageFile == null || Course.ImageFile.Length == 0)
            {
                return null;
            }

            try
            {
                var uploadsFolder = Path.Combine("wwwroot", "images", "courses");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(Course.ImageFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await Course.ImageFile.CopyToAsync(fileStream);
                }

                return $"/images/courses/{uniqueFileName}";
            }
            catch (Exception ex)
            {
                // Log the error if needed
                ModelState.AddModelError("Course.ImageFile", $"Error saving image: {ex.Message}");
                return null;
            }
        }

        private class Category
        {
            public int CategoryId { get; set; }
            public string Name { get; set; }
        }
    }
}