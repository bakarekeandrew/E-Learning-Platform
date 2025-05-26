using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages
{
    [Authorize(Policy = "Permission_COURSE.VIEW")]
    public class CourseInfoModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<CourseInfoModel> _logger;
        private readonly IPermissionService _permissionService;

        public CourseInfoModel(ILogger<CourseInfoModel> logger, IPermissionService permissionService)
        {
            _connectionString = "Data Source=ABAKAREKE_25497\\SQLEXPRESS;" +
                              "Initial Catalog=ONLINE_LEARNING_PLATFORM;" +
                              "Integrated Security=True;" +
                              "TrustServerCertificate=True";
            _logger = logger;
            _permissionService = permissionService;
        }

        // Properties
        public List<Course> Courses { get; set; } = new();
        public List<SelectListItem> Instructors { get; set; } = new();
        public List<SelectListItem> Categories { get; set; } = new();
        public PaginationInfo Pagination { get; set; } = new();
        public FilterOptions Filters { get; set; } = new();

        // Permission properties
        public bool CanViewCourses { get; set; }
        public bool CanManageCourses { get; set; }
        public bool CanCreateCourses { get; set; }
        public bool CanEditCourses { get; set; }
        public bool CanDeleteCourses { get; set; }

        [BindProperty]
        public CourseInputModel CourseInput { get; set; } = new();

        // Models
        public class Course
        {
            public int CourseId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int CreatedBy { get; set; }
            public string InstructorName { get; set; } = string.Empty;
            public int? CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public string ThumbnailUrl { get; set; } = string.Empty;
            public string Requirements { get; set; } = string.Empty;
            public DateTime CreationDate { get; set; }
            public bool IsActive { get; set; }
            public int StudentCount { get; set; }
            public decimal? Rating { get; set; }
        }

        public class CourseInputModel
        {
            public int CourseId { get; set; }

            [Required]
            [StringLength(255)]
            public string Title { get; set; } = string.Empty;

            [Required]
            public string Description { get; set; } = string.Empty;

            [Required]
            [Range(1, int.MaxValue)]
            public int CreatedBy { get; set; }

            public int? CategoryId { get; set; }
            public string ThumbnailUrl { get; set; } = string.Empty;
            public string Requirements { get; set; } = string.Empty;
            public bool IsActive { get; set; } = true;
        }

        public class PaginationInfo
        {
            public int CurrentPage { get; set; } = 1;
            public int PageSize { get; set; } = 10;
            public int TotalItems { get; set; }
            public int TotalPages => (int)Math.Ceiling(TotalItems / (double)PageSize);
        }

        public class FilterOptions
        {
            public string SearchTerm { get; set; } = string.Empty;
            public string StatusFilter { get; set; } = string.Empty;
        }

        private async Task<int> GetCurrentUserIdAsync()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("User ID claim not found or invalid");
                throw new InvalidOperationException("User not properly authenticated");
            }
            return userId;
        }

        private async Task LoadPermissionsAsync()
        {
            try
            {
                var userId = await GetCurrentUserIdAsync();
                
                // Check each permission using PermissionService
                CanViewCourses = await _permissionService.HasPermissionAsync(userId, "COURSE.VIEW");
                CanManageCourses = await _permissionService.HasPermissionAsync(userId, "COURSE.MANAGE");
                CanCreateCourses = await _permissionService.HasPermissionAsync(userId, "COURSE.CREATE");
                CanEditCourses = await _permissionService.HasPermissionAsync(userId, "COURSE.EDIT");
                CanDeleteCourses = await _permissionService.HasPermissionAsync(userId, "COURSE.DELETE");

                //_logger.LogInfo("CourseInfo", $"User {userId} permissions loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("CourseInfo", $"Error loading permissions: {ex.Message}");
                CanViewCourses = CanManageCourses = CanCreateCourses = CanEditCourses = CanDeleteCourses = false;
            }
        }

        public async Task<IActionResult> OnGetAsync(
            string searchTerm = "",
            string statusFilter = "",
            int pageNumber = 1)
        {
            try
            {
                var userId = await GetCurrentUserIdAsync();
                
                // Load user permissions
                await LoadPermissionsAsync();

                // Check if user has basic view permission
                if (!CanViewCourses)
                {
                    _logger.LogWarning("Access Denied", $"User {userId} attempted to access CourseInfo without view permission");
                    return Forbid();
                }

                Filters.SearchTerm = searchTerm;
                Filters.StatusFilter = statusFilter;
                Pagination.CurrentPage = pageNumber < 1 ? 1 : pageNumber;

                await LoadInstructorsAsync();
                await LoadCategoriesAsync();
                await LoadCoursesAsync();

                return Page();
            }
            catch (InvalidOperationException)
            {
                return RedirectToPage("/Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accessing CourseInfo page");
                return RedirectToPage("/Error");
            }
        }

        public async Task<IActionResult> OnPostAdd()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            if (!await _permissionService.HasPermissionAsync(userId, "COURSE.CREATE"))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                await LoadInstructorsAsync();
                await LoadCategoriesAsync();
                await LoadCoursesAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO COURSES (
                        TITLE, DESCRIPTION, CREATED_BY, CATEGORY_ID, 
                        THUMBNAIL_URL, REQUIREMENTS, IS_ACTIVE
                    ) VALUES (
                        @Title, @Description, @CreatedBy, @CategoryId,
                        @ThumbnailUrl, @Requirements, @IsActive
                    )";

                await connection.ExecuteAsync(query, new
                {
                    CourseInput.Title,
                    CourseInput.Description,
                    CourseInput.CreatedBy,
                    CourseInput.CategoryId,
                    CourseInput.ThumbnailUrl,
                    CourseInput.Requirements,
                    CourseInput.IsActive
                });

                TempData["SuccessMessage"] = "Course added successfully!";
                return RedirectToPage();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error: {ex.Message}");
                await LoadInstructorsAsync();
                await LoadCategoriesAsync();
                await LoadCoursesAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostUpdate()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            if (!await _permissionService.HasPermissionAsync(userId, "COURSE.EDIT"))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                await LoadInstructorsAsync();
                await LoadCategoriesAsync();
                await LoadCoursesAsync();
                return Page();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE COURSES SET 
                        TITLE = @Title,
                        DESCRIPTION = @Description,
                        CREATED_BY = @CreatedBy,
                        CATEGORY_ID = @CategoryId,
                        THUMBNAIL_URL = @ThumbnailUrl,
                        REQUIREMENTS = @Requirements,
                        IS_ACTIVE = @IsActive
                    WHERE COURSE_ID = @CourseId";

                await connection.ExecuteAsync(query, new
                {
                    CourseInput.Title,
                    CourseInput.Description,
                    CourseInput.CreatedBy,
                    CourseInput.CategoryId,
                    CourseInput.ThumbnailUrl,
                    CourseInput.Requirements,
                    CourseInput.IsActive,
                    CourseInput.CourseId
                });

                TempData["SuccessMessage"] = "Course updated successfully!";
                return RedirectToPage();
            }
            catch (SqlException ex)
            {
                ModelState.AddModelError("", $"Database error: {ex.Message}");
                await LoadInstructorsAsync();
                await LoadCategoriesAsync();
                await LoadCoursesAsync();
                return Page();
            }
        }

        public async Task<IActionResult> OnPostDelete(int courseId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            if (!await _permissionService.HasPermissionAsync(userId, "COURSE.DELETE"))
            {
                return Forbid();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(
                    "DELETE FROM COURSES WHERE COURSE_ID = @CourseId",
                    new { CourseId = courseId });

                TempData["SuccessMessage"] = "Course deleted successfully!";
                return RedirectToPage();
            }
            catch (SqlException ex)
            {
                TempData["ErrorMessage"] = $"Error deleting course: {ex.Message}";
                return RedirectToPage();
            }
        }

        public async Task<JsonResult> OnGetCourseById(int courseId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        COURSE_ID as CourseId,
                        TITLE as Title,
                        DESCRIPTION as Description,
                        CREATED_BY as CreatedBy,
                        CATEGORY_ID as CategoryId,
                        THUMBNAIL_URL as ThumbnailUrl,
                        REQUIREMENTS as Requirements,
                        IS_ACTIVE as IsActive
                    FROM COURSES
                    WHERE COURSE_ID = @CourseId";

                var course = await connection.QueryFirstOrDefaultAsync<CourseInputModel>(query, new { CourseId = courseId });

                if (course == null)
                {
                    return new JsonResult(new { error = "Course not found" });
                }

                return new JsonResult(course);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(int courseId, bool currentStatus)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return Forbid();
            }

            if (!await _permissionService.HasPermissionAsync(userId, "COURSE.EDIT"))
            {
                return Forbid();
            }

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Update the course's status
                var newStatus = !currentStatus;
                var result = await connection.ExecuteAsync(
                    "UPDATE COURSES SET IS_ACTIVE = @NewStatus WHERE COURSE_ID = @CourseId",
                    new { NewStatus = newStatus, CourseId = courseId });

                if (result > 0)
                {
                    TempData["SuccessMessage"] = $"Course has been {(newStatus ? "activated" : "deactivated")} successfully.";
                    // Return JSON response for AJAX requests
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new JsonResult(new { success = true, newStatus = newStatus });
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = "Failed to update course status.";
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return new JsonResult(new { success = false, message = "Failed to update course status." });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling course status");
                TempData["ErrorMessage"] = "An error occurred while updating course status.";
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return new JsonResult(new { success = false, message = "An error occurred while updating course status." });
                }
            }

            // Redirect back to the page for non-AJAX requests
            return RedirectToPage();
        }

        private async Task LoadInstructorsAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT u.USER_ID as Value, u.FULL_NAME as Text 
                FROM USERS u
                JOIN ROLES r ON u.ROLE_ID = r.ROLE_ID
                WHERE r.ROLE_NAME = 'Instructor'
                ORDER BY u.FULL_NAME";

            var instructors = await connection.QueryAsync<SelectListItem>(query);
            Instructors = instructors.ToList();
        }

        private async Task LoadCategoriesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT CATEGORY_ID as Value, NAME as Text FROM COURSE_CATEGORIES ORDER BY NAME";
            var categories = await connection.QueryAsync<SelectListItem>(query);
            Categories = categories.ToList();
        }

        private async Task LoadCoursesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Build where clause
            var whereClauses = new List<string> { "1=1" };
            var parameters = new DynamicParameters();

            // Apply filters
            if (!string.IsNullOrEmpty(Filters.SearchTerm))
            {
                whereClauses.Add("(c.TITLE LIKE @SearchTerm OR c.DESCRIPTION LIKE @SearchTerm)");
                parameters.Add("SearchTerm", $"%{Filters.SearchTerm}%");
            }

            if (!string.IsNullOrEmpty(Filters.StatusFilter))
            {
                if (Filters.StatusFilter == "Active")
                {
                    whereClauses.Add("c.IS_ACTIVE = 1");
                }
                else if (Filters.StatusFilter == "Inactive")
                {
                    whereClauses.Add("c.IS_ACTIVE = 0");
                }
            }

            var whereClause = string.Join(" AND ", whereClauses);

            // Count total courses
            var countQuery = $@"
                SELECT COUNT(*)
                FROM COURSES c
                WHERE {whereClause}";

            Pagination.TotalItems = await connection.ExecuteScalarAsync<int>(countQuery, parameters);

            // Get paginated courses with student count and rating
            var offset = (Pagination.CurrentPage - 1) * Pagination.PageSize;
            parameters.Add("Offset", offset);
            parameters.Add("PageSize", Pagination.PageSize);

            var selectQuery = $@"
                SELECT 
                    c.COURSE_ID as CourseId,
                    c.TITLE as Title,
                    c.DESCRIPTION as Description,
                    c.CREATED_BY as CreatedBy,
                    u.FULL_NAME as InstructorName,
                    c.CATEGORY_ID as CategoryId,
                    cat.NAME as CategoryName,
                    c.THUMBNAIL_URL as ThumbnailUrl,
                    c.REQUIREMENTS as Requirements,
                    c.CREATION_DATE as CreationDate,
                    c.IS_ACTIVE as IsActive,
                    ISNULL((SELECT COUNT(*) FROM COURSE_ENROLLMENTS WHERE COURSE_ID = c.COURSE_ID), 0) as StudentCount,
                    ISNULL((SELECT AVG(CAST(RATING AS DECIMAL(3,1))) FROM REVIEWS WHERE COURSE_ID = c.COURSE_ID), 0) as Rating
                FROM COURSES c
                LEFT JOIN USERS u ON c.CREATED_BY = u.USER_ID
                LEFT JOIN COURSE_CATEGORIES cat ON c.CATEGORY_ID = cat.CATEGORY_ID
                WHERE {whereClause}
                ORDER BY c.CREATION_DATE DESC
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            Courses = (await connection.QueryAsync<Course>(selectQuery, parameters)).ToList();
        }
    }
}