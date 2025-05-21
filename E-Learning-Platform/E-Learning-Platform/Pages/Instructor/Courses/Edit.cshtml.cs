using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Dapper;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Http;

namespace E_Learning_Platform.Pages.Instructor.Courses
{
    public class EditModel : PageModel
    {
        private readonly string _connectionString;

        public EditModel()
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
        }

        [BindProperty]
        public CourseInput Course { get; set; } = new CourseInput();

        public SelectList Categories { get; set; }
        public SelectList Levels { get; set; }
        public string CurrentImageUrl { get; set; }

        public class CourseInput
        {
            public int CourseId { get; set; }

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

        public async Task<IActionResult> OnGetAsync(int id)
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get course details
                var course = await connection.QueryFirstOrDefaultAsync<CourseDetails>(@"
                    SELECT 
                        COURSE_ID AS CourseId,
                        TITLE AS Title,
                        DESCRIPTION AS Description,
                        CATEGORY_ID AS CategoryId,
                        REQUIREMENTS AS Level,
                        IS_ACTIVE AS IsActive,
                        THUMBNAIL_URL AS ImageUrl
                    FROM COURSES
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { CourseId = id, InstructorId = userId });

                if (course == null)
                {
                    return NotFound();
                }

                // Map to input model
                Course.CourseId = course.CourseId;
                Course.Title = course.Title;
                Course.Description = course.Description;
                Course.CategoryId = course.CategoryId;
                Course.Level = course.Level;
                Course.IsActive = course.IsActive;
                CurrentImageUrl = course.ImageUrl;

                await LoadCategories();
                LoadLevels();

                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred: {ex.Message}");
                return RedirectToPage("./Index");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            int? userId = null;
            byte[] userIdBytes;
            if (HttpContext.Session.TryGetValue("UserId", out userIdBytes))
            {
                userId = BitConverter.ToInt32(userIdBytes, 0);
            }
            if (userId == null)
            {
                return RedirectToPage("/Login");
            }

            if (!ModelState.IsValid)
            {
                await LoadCategories();
                LoadLevels();
                return Page();
            }

            try
            {
                // Save new image if provided
                string imagePath = null;
                if (Course.ImageFile != null && Course.ImageFile.Length > 0)
                {
                    imagePath = await SaveImage();
                }

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Verify course ownership
                var ownsCourse = await connection.ExecuteScalarAsync<bool>(@"
                    SELECT COUNT(1) FROM COURSES 
                    WHERE COURSE_ID = @CourseId AND CREATED_BY = @InstructorId",
                    new { Course.CourseId, InstructorId = userId });

                if (!ownsCourse)
                {
                    return NotFound();
                }

                // Update the course
                await connection.ExecuteAsync(@"
                    UPDATE COURSES SET
                        TITLE = @Title,
                        DESCRIPTION = @Description,
                        CATEGORY_ID = @CategoryId,
                        REQUIREMENTS = @Level,
                        IS_ACTIVE = @IsActive
                        " + (imagePath != null ? ", THUMBNAIL_URL = @ImageUrl" : "") + @"
                    WHERE COURSE_ID = @CourseId",
                    new
                    {
                        Course.CourseId,
                        Course.Title,
                        Course.Description,
                        Course.CategoryId,
                        Course.Level,
                        Course.IsActive,
                        ImageUrl = imagePath
                    });

                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"An error occurred while updating the course: {ex.Message}");
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

        private class CourseDetails
        {
            public int CourseId { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public int CategoryId { get; set; }
            public string Level { get; set; }
            public bool IsActive { get; set; }
            public string ImageUrl { get; set; }
        }
    }
}