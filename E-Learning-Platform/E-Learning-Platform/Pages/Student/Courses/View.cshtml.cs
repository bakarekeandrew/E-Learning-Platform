using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace E_Learning_Platform.Pages.Student.Courses
{
    public class ViewModel : PageModel
    {
        private readonly string _connectionString;
        private readonly ILogger<ViewModel> _logger;

        public ViewModel(ILogger<ViewModel> logger, IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        // Model properties
        public CourseDetails Course { get; set; }
        public List<Module> Modules { get; set; } = new List<Module>();
        public List<CourseAssignment> Assignments { get; set; } = new List<CourseAssignment>();
        public List<Announcement> Announcements { get; set; } = new List<Announcement>();
        public string ErrorMessage { get; set; }
        public int CurrentUserId { get; set; }
        public string ContinueUrl { get; set; }
        public int? ContinueModuleId { get; set; }
        public ContentItem NextContentItem { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            _logger.LogInformation("OnGetAsync started for course ID {CourseId}.", id);
            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                _logger.LogWarning("User not authenticated for course view {CourseId}.", id);
                return RedirectToPage("/Login");
            }

            CurrentUserId = BitConverter.ToInt32(userIdBytes, 0);
            _logger.LogInformation("User {UserId} authenticated for course {CourseId}.", CurrentUserId, id);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                _logger.LogInformation("Opening SQL connection for course {CourseId}, user {UserId}.", id, CurrentUserId);
                await connection.OpenAsync();
                _logger.LogInformation("SQL connection opened for course {CourseId}, user {UserId}.", id, CurrentUserId);

                // Check enrollment
                _logger.LogInformation("Checking enrollment for user {UserId} in course {CourseId}.", CurrentUserId, id);
                var isEnrolled = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(1) FROM COURSE_ENROLLMENTS WHERE USER_ID = @UserId AND COURSE_ID = @CourseId",
                    new { UserId = CurrentUserId, CourseId = id });

                if (!isEnrolled)
                {
                    _logger.LogWarning("User {UserId} not enrolled in course {CourseId}. Redirecting.", CurrentUserId, id);
                    ErrorMessage = "You are not enrolled in this course.";
                    return RedirectToPage("/Student/Courses");
                }
                _logger.LogInformation("User {UserId} is enrolled in course {CourseId}.", CurrentUserId, id);

                // Load course details
                _logger.LogInformation("Attempting to load course details for course {CourseId}, user {UserId}.", id, CurrentUserId);
                Course = await connection.QueryFirstOrDefaultAsync<CourseDetails>(@"
                    SELECT 
                        c.COURSE_ID AS CourseId,
                        c.TITLE AS Title,
                        c.DESCRIPTION AS Description,
                        c.THUMBNAIL_URL AS ThumbnailUrl,
                        u.FULL_NAME AS Instructor,
                        u.EMAIL AS InstructorEmail,
                        ce.ENROLLMENT_DATE AS EnrollmentDate,
                        ISNULL(cp.PROGRESS, 0) AS Progress,
                        ce.STATUS AS Status
                    FROM COURSES c
                    JOIN USERS u ON c.CREATED_BY = u.USER_ID
                    JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @UserId
                    LEFT JOIN COURSE_PROGRESS cp ON c.COURSE_ID = cp.COURSE_ID AND cp.USER_ID = @UserId
                    WHERE c.COURSE_ID = @CourseId",
                    new { CourseId = id, UserId = CurrentUserId });

                if (Course == null)
                {
                    _logger.LogWarning("Course details not found for course {CourseId}. Returning NotFound.", id);
                    return NotFound();
                }
                _logger.LogInformation("Successfully loaded course details for course {CourseId}: {CourseTitle}", id, Course.Title);

                // Update last accessed time
                _logger.LogInformation("Attempting to update last accessed time for course {CourseId}, user {UserId}.", id, CurrentUserId);
                await connection.ExecuteAsync(@"
                    UPDATE COURSE_PROGRESS 
                    SET LAST_ACCESSED = GETDATE() 
                    WHERE USER_ID = @UserId AND COURSE_ID = @CourseId;
                    
                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO COURSE_PROGRESS (USER_ID, COURSE_ID, PROGRESS, LAST_ACCESSED)
                        VALUES (@UserId, @CourseId, 0, GETDATE())
                    END",
                    new { UserId = CurrentUserId, CourseId = id });
                _logger.LogInformation("Successfully updated last accessed time for course {CourseId}, user {UserId}.", id, CurrentUserId);

                // Load modules with progress
                _logger.LogInformation("Attempting to load modules for course {CourseId}, user {UserId}.", id, CurrentUserId);
                var modules = (await connection.QueryAsync<Module>(@"
                    SELECT 
                        m.MODULE_ID AS ModuleId,
                        m.TITLE AS Title,
                        m.DESCRIPTION AS Description,
                        m.SEQUENCE_NUMBER AS OrderSequence,
                        CASE 
                            WHEN up.STATUS = 'completed' THEN 'Completed'
                            WHEN up.STATUS = 'in_progress' THEN 'In Progress'
                            ELSE 'Not Started'
                        END AS CompletionStatus
                    FROM MODULES m
                    LEFT JOIN USER_MODULE_PROGRESS up ON m.MODULE_ID = up.MODULE_ID AND up.USER_ID = @UserId
                    WHERE m.COURSE_ID = @CourseId
                    ORDER BY m.SEQUENCE_NUMBER",
                    new { CourseId = id, UserId = CurrentUserId })).ToList();
                _logger.LogInformation("Found {ModuleCount} modules raw from DB for course {CourseId}.", modules.Count, id);

                // Load quizzes for these modules (Resources part removed)
                var allContentItems = new List<Content>();

                // Get quizzes
                _logger.LogInformation("Attempting to load quizzes for modules in course {CourseId}, user {UserId}.", id, CurrentUserId);
                var quizzes = await connection.QueryAsync<Content>(@"
                    SELECT 
                        q.QUIZ_ID AS ContentId,
                        q.MODULE_ID AS ModuleId,
                        q.TITLE AS ContentTitle,
                        'Quiz' AS ContentType,
                        'quiz' AS ItemType,
                        1000 + ROW_NUMBER() OVER (PARTITION BY q.MODULE_ID ORDER BY q.QUIZ_ID) AS SequenceNumber,
                        CASE WHEN 
                            EXISTS (SELECT 1 FROM QUIZ_ATTEMPTS qa 
                                    WHERE qa.QUIZ_ID = q.QUIZ_ID AND qa.USER_ID = @UserId AND qa.PASSED = 1) 
                        THEN 1 ELSE 0 END AS IsCompleted
                    FROM QUIZZES q
                    WHERE q.MODULE_ID IN (SELECT MODULE_ID FROM MODULES WHERE COURSE_ID = @CourseId)",
                    new { CourseId = id, UserId = CurrentUserId });
                allContentItems.AddRange(quizzes);
                _logger.LogInformation("Successfully loaded {QuizCount} quizzes for course {CourseId}.", quizzes.Count(), id);

                // Organize content into modules
                _logger.LogInformation("Attempting to organize content into modules for course {CourseId}.", id);
                var moduleDict = modules.ToDictionary(m => m.ModuleId);
                foreach (var content in allContentItems.OrderBy(c => c.ModuleId).ThenBy(c => c.SequenceNumber))
                {
                    if (moduleDict.TryGetValue(content.ModuleId, out var module))
                    {
                        module.Contents.Add(content);
                    }
                }
                Modules = modules;
                _logger.LogInformation("Successfully organized content. Final module count for display: {ModuleCount} for course {CourseId}.", Modules.Count, id);

                // Load other course data (Assignments and Announcements)
                _logger.LogInformation("Attempting to load assignments for course {CourseId}, user {UserId}.", id, CurrentUserId);
                Assignments = (await connection.QueryAsync<CourseAssignment>(@"
            SELECT 
                a.ASSIGNMENT_ID AS AssignmentId,
                a.TITLE AS Title,
                a.INSTRUCTIONS AS Instructions,
                a.DUE_DATE AS DueDate,
                a.MAX_SCORE AS MaxScore,
                NULL AS ModuleTitle,
                NULL AS ModuleId,
                CASE 
                    WHEN s.SUBMISSION_ID IS NOT NULL AND s.GRADE IS NOT NULL THEN 'Graded'
                    WHEN s.SUBMISSION_ID IS NOT NULL THEN 'Submitted'
                    WHEN a.DUE_DATE < GETDATE() THEN 'Overdue'
                    ELSE 'Pending'
                END AS Status,
                ISNULL(s.GRADE, 0) AS Grade,
                ISNULL(s.FEEDBACK, '') AS Feedback,
                s.SUBMITTED_ON AS SubmittedOn
            FROM ASSIGNMENTS a
            LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND s.USER_ID = @UserId
            WHERE a.COURSE_ID = @CourseId
            ORDER BY a.DUE_DATE",
            new { CourseId = id, UserId = CurrentUserId })).ToList();
                _logger.LogInformation("Successfully loaded {AssignmentCount} assignments for course {CourseId}.", Assignments.Count, id);

                _logger.LogInformation("Attempting to load announcements for course {CourseId}.", id);
                Announcements = (await connection.QueryAsync<Announcement>(@"
                    SELECT 
                        a.ANNOUNCEMENT_ID AS AnnouncementId,
                        a.TITLE AS Title,
                        a.CONTENT AS Content,
                        u.FULL_NAME AS PostedBy,
                        a.POSTED_DATE AS PostedDate
                    FROM ANNOUNCEMENTS a
                    JOIN USERS u ON a.POSTED_BY = u.USER_ID
                    WHERE a.COURSE_ID = @CourseId
                    ORDER BY a.POSTED_DATE DESC",
                    new { CourseId = id })).ToList();
                _logger.LogInformation("Successfully loaded {AnnouncementCount} announcements for course {CourseId}.", Announcements.Count, id);

                // Determine where user should continue
                _logger.LogInformation("Attempting to determine next content item for course {CourseId}, user {UserId}.", id, CurrentUserId);
                await DetermineNextContentItem(connection, id);
                _logger.LogInformation("Successfully determined next content item for course {CourseId}.", id);

                // Calculate and update course progress
                _logger.LogInformation("Attempting to update overall course progress for course {CourseId}, user {UserId}.", id, CurrentUserId);
                await UpdateCourseProgress(connection, id);
                _logger.LogInformation("Successfully updated overall course progress for course {CourseId}.", id);

                _logger.LogInformation("OnGetAsync completed successfully for course {CourseId}, user {UserId}.", id, CurrentUserId);
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading course details for course ID {CourseId}, User {UserId}", id, CurrentUserId);
                ErrorMessage = "An error occurred while loading course details.";
                return Page();
            }
        }

        private async Task DetermineNextContentItem(SqlConnection connection, int courseId)
        {
            _logger.LogDebug("Determining next content item for CourseId: {CourseId}, UserId: {UserId}", courseId, CurrentUserId);
            NextContentItem = null;
            ContinueUrl = null;
            ContinueModuleId = null;

            // Attempt to find the most recently accessed *incomplete* quiz by the user in this course
            var lastAccessedIncompleteQuiz = await connection.QueryFirstOrDefaultAsync<ContentItemDetails>(@"
                SELECT TOP 1 
                    q.MODULE_ID AS ModuleId, 
                    m.TITLE AS ModuleTitle,
                    q.QUIZ_ID AS ItemId, 
                    q.TITLE AS ItemTitle,
                    'quiz' AS ItemType
                FROM USER_PROGRESS up
                JOIN QUIZZES q ON up.QUIZ_ID = q.QUIZ_ID
                JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                WHERE up.USER_ID = @UserId 
                  AND m.COURSE_ID = @CourseId 
                  AND up.STATUS <> 'completed' 
                  AND up.QUIZ_ID IS NOT NULL
                ORDER BY up.LAST_ACCESSED DESC", 
                new { UserId = CurrentUserId, CourseId = courseId });

            if (lastAccessedIncompleteQuiz != null)
            {
                _logger.LogDebug("Found last accessed incomplete quiz: {ItemTitle} in module {ModuleTitle}", lastAccessedIncompleteQuiz.ItemTitle, lastAccessedIncompleteQuiz.ModuleTitle);
                NextContentItem = new ContentItem
                {
                    ModuleId = lastAccessedIncompleteQuiz.ModuleId,
                    ModuleTitle = lastAccessedIncompleteQuiz.ModuleTitle,
                    ItemId = lastAccessedIncompleteQuiz.ItemId,
                    ItemTitle = lastAccessedIncompleteQuiz.ItemTitle,
                    ItemType = lastAccessedIncompleteQuiz.ItemType
                };
                ContinueUrl = $"/Student/Courses/Quiz?id={NextContentItem.ItemId}"; // Corrected from Quiz/{id} to Quiz?id=
                ContinueModuleId = NextContentItem.ModuleId;
                return;
            }
            _logger.LogDebug("No recently accessed incomplete quiz found.");

            // Fallback: Find the first quiz in the earliest module that the user hasn't passed yet
            var firstUnpassedQuiz = await connection.QueryFirstOrDefaultAsync<ContentItemDetails>(@"
                SELECT TOP 1 
                    m.MODULE_ID AS ModuleId, 
                    m.TITLE AS ModuleTitle,
                    q.QUIZ_ID AS ItemId, 
                    q.TITLE AS ItemTitle,
                    'quiz' AS ItemType
                FROM MODULES m
                JOIN QUIZZES q ON m.MODULE_ID = q.MODULE_ID
                WHERE m.COURSE_ID = @CourseId 
                  AND NOT EXISTS (
                      SELECT 1 FROM QUIZ_ATTEMPTS qa 
                      WHERE qa.QUIZ_ID = q.QUIZ_ID AND qa.USER_ID = @UserId AND qa.PASSED = 1
                  )
                ORDER BY m.SEQUENCE_NUMBER, q.QUIZ_ID ASC -- Assuming QUIZ_ID implies sequence within module for quizzes
                ", 
                new { UserId = CurrentUserId, CourseId = courseId });

            if (firstUnpassedQuiz != null)
            {
                _logger.LogDebug("Found first unpassed quiz: {ItemTitle} in module {ModuleTitle}", firstUnpassedQuiz.ItemTitle, firstUnpassedQuiz.ModuleTitle);
                NextContentItem = new ContentItem
                {
                    ModuleId = firstUnpassedQuiz.ModuleId,
                    ModuleTitle = firstUnpassedQuiz.ModuleTitle,
                    ItemId = firstUnpassedQuiz.ItemId,
                    ItemTitle = firstUnpassedQuiz.ItemTitle,
                    ItemType = firstUnpassedQuiz.ItemType
                };
                ContinueUrl = $"/Student/Courses/Quiz?id={NextContentItem.ItemId}";
                ContinueModuleId = NextContentItem.ModuleId;
                return;
            }
            _logger.LogDebug("No unpassed quizzes found. All quizzes might be completed or course has no quizzes.");

            // If all quizzes are done or course has no quizzes, set ContinueModuleId to the first module to expand it by default.
            var firstModule = Modules.OrderBy(m => m.OrderSequence).FirstOrDefault();
            if (firstModule != null)
            {
                _logger.LogDebug("Setting continue/highlight to first module: {ModuleTitle}", firstModule.Title);
                ContinueModuleId = firstModule.ModuleId;
                // ContinueUrl remains null, as there's no specific content item to jump to, just the course view highlighting the module.
            }
            else
            {
                _logger.LogDebug("No modules found in the course to set as continue point.");
            }
        }

        // Helper class for QueryFirstOrDefaultAsync in DetermineNextContentItem
        private class ContentItemDetails
        {
            public int ModuleId { get; set; }
            public string ModuleTitle { get; set; }
            public int ItemId { get; set; }
            public string ItemTitle { get; set; }
            public string ItemType { get; set; }
        }

        private async Task UpdateCourseProgress(SqlConnection connection, int courseId)
        {
            _logger.LogInformation("Updating course progress for CourseId: {CourseId}, UserId: {UserId}", courseId, CurrentUserId);

            // --- Calculate Total Items --- 
            var totalModules = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM MODULES WHERE COURSE_ID = @CourseId",
                new { CourseId = courseId });

            var totalQuizzesInCourse = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(q.QUIZ_ID) FROM QUIZZES q JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID WHERE m.COURSE_ID = @CourseId",
                new { CourseId = courseId });

            var totalAssignmentsInCourse = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ASSIGNMENTS WHERE COURSE_ID = @CourseId",
                new { CourseId = courseId });

            float totalCountableItems = totalModules + totalQuizzesInCourse + totalAssignmentsInCourse;
            _logger.LogDebug("Total countable items for course {CourseId}: Modules={TM}, Quizzes={TQ}, Assignments={TA}, Sum={Sum}", 
                courseId, totalModules, totalQuizzesInCourse, totalAssignmentsInCourse, totalCountableItems);

            // --- Calculate Completed Items --- 
            var completedModulesCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM USER_MODULE_PROGRESS ump
                JOIN MODULES m ON ump.MODULE_ID = m.MODULE_ID
                WHERE ump.USER_ID = @UserId AND m.COURSE_ID = @CourseId AND ump.STATUS = 'completed'",
                new { UserId = CurrentUserId, CourseId = courseId });

            var passedQuizzesCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(DISTINCT q.QUIZ_ID) 
                FROM QUIZ_ATTEMPTS qa
                JOIN QUIZZES q ON qa.QUIZ_ID = q.QUIZ_ID
                JOIN MODULES m ON q.MODULE_ID = m.MODULE_ID
                WHERE m.COURSE_ID = @CourseId AND qa.USER_ID = @UserId AND qa.PASSED = 1",
                new { UserId = CurrentUserId, CourseId = courseId });

            var gradedAssignmentsCount = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(DISTINCT a.ASSIGNMENT_ID) 
                FROM ASSIGNMENT_SUBMISSIONS s 
                JOIN ASSIGNMENTS a ON s.ASSIGNMENT_ID = a.ASSIGNMENT_ID 
                WHERE a.COURSE_ID = @CourseId AND s.USER_ID = @UserId AND s.GRADE IS NOT NULL",
                new { UserId = CurrentUserId, CourseId = courseId });

            float completedCountableItems = completedModulesCount + passedQuizzesCount + gradedAssignmentsCount;
            _logger.LogDebug("Completed countable items for course {CourseId}: Modules={CM}, Quizzes={CQ}, Assignments={CA}, Sum={Sum}",
                courseId, completedModulesCount, passedQuizzesCount, gradedAssignmentsCount, completedCountableItems);
            
            decimal calculatedProgress = 0;
            if (totalCountableItems > 0)
            {
                calculatedProgress = (decimal)(completedCountableItems / totalCountableItems) * 100;
            }
            _logger.LogInformation("Calculated progress for course {CourseId} for user {UserId}: {Progress}%", courseId, CurrentUserId, calculatedProgress);

            // Update course progress in the COURSE_PROGRESS table
            await connection.ExecuteAsync(@"
                MERGE COURSE_PROGRESS AS target
                USING (SELECT @UserId AS USER_ID, @CourseId AS COURSE_ID) AS source
                ON (target.USER_ID = source.USER_ID AND target.COURSE_ID = source.COURSE_ID)
                WHEN MATCHED THEN
                    UPDATE SET PROGRESS = @Progress, LAST_ACCESSED = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (USER_ID, COURSE_ID, PROGRESS, LAST_ACCESSED)
                    VALUES (@UserId, @CourseId, @Progress, GETDATE());",
                new { UserId = CurrentUserId, CourseId = courseId, Progress = calculatedProgress });
            _logger.LogInformation("COURSE_PROGRESS table updated for CourseId {CourseId}, UserId {UserId} with progress {Progress}%", courseId, CurrentUserId, calculatedProgress);

            // Update the local Course object's Progress property if it's loaded and matches the current courseId
            if (Course != null && Course.CourseId == courseId)
            {
                Course.Progress = calculatedProgress;
            }
        }

        private async Task CheckModuleCompletion(SqlConnection connection, int userId, int moduleId)
        {
            _logger.LogInformation("Checking module completion for ModuleId: {ModuleId}, UserId: {UserId}", moduleId, userId);
            
            // Count all quizzes in this module
            var totalQuizzesInModule = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM QUIZZES WHERE MODULE_ID = @ModuleId",
                new { ModuleId = moduleId });
            _logger.LogDebug("Total quizzes in module {ModuleId}: {TotalQuizzesInModule}", moduleId, totalQuizzesInModule);

            if (totalQuizzesInModule == 0) 
            {
                // If a module has no quizzes, it's considered complete for progress purposes.
                // (Previously, resource completion would also be checked here if resources were part of progress)
                _logger.LogInformation("Module {ModuleId} has no quizzes, marking as complete by default for UserId {UserId}.", moduleId, userId);
                await MarkModuleAsCompleteInDb(connection, userId, moduleId);
                return;
            }

            // Count passed quizzes in this module by the user
            var passedQuizzesInModule = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(DISTINCT q.QUIZ_ID) FROM QUIZZES q
                INNER JOIN QUIZ_ATTEMPTS qa ON q.QUIZ_ID = qa.QUIZ_ID
                WHERE q.MODULE_ID = @ModuleId AND qa.USER_ID = @UserId AND qa.PASSED = 1",
                new { UserId = userId, ModuleId = moduleId });
            _logger.LogDebug("Passed quizzes in module {ModuleId} by UserId {UserId}: {PassedQuizzesInModule}", moduleId, userId, passedQuizzesInModule);

            // If all quizzes defined for the module are passed, mark the module complete.
            if (passedQuizzesInModule >= totalQuizzesInModule)
            {
                _logger.LogInformation("All quizzes in module {ModuleId} passed by UserId {UserId}. Marking module as complete.", moduleId, userId);
                await MarkModuleAsCompleteInDb(connection, userId, moduleId);
            }
            // If not all quizzes are passed, USER_MODULE_PROGRESS remains unchanged or in its current state (e.g. 'in_progress' if it was set by other means)
        }

        private async Task MarkModuleAsCompleteInDb(SqlConnection connection, int userId, int moduleId)
        {
            await connection.ExecuteAsync(@"
                MERGE USER_MODULE_PROGRESS AS target
                USING (SELECT @UserId AS USER_ID, @ModuleId AS MODULE_ID) AS source
                ON (target.USER_ID = source.USER_ID AND target.MODULE_ID = source.MODULE_ID)
                WHEN MATCHED THEN
                    UPDATE SET STATUS = 'completed', 
                               COMPLETED_ON = ISNULL(target.COMPLETED_ON, GETDATE()), 
                               LAST_ACCESSED = GETDATE()
                WHEN NOT MATCHED THEN
                    INSERT (USER_ID, MODULE_ID, STATUS, COMPLETED_ON, LAST_ACCESSED)
                    VALUES (@UserId, @ModuleId, 'completed', GETDATE(), GETDATE());",
                new { UserId = userId, ModuleId = moduleId });
            _logger.LogInformation("Module {ModuleId} ensured as 'completed' in USER_MODULE_PROGRESS for UserId {UserId}.", moduleId, userId);
        }

        public async Task<IActionResult> SubmitQuizAttempt(int quizId, int score, int totalQuestions)
        {
            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }
            int userId = BitConverter.ToInt32(userIdBytes, 0);
            _logger.LogInformation("Submitting quiz attempt for QuizId: {QuizId}, UserId: {UserId}, Score: {Score}/{TotalQuestions}", quizId, userId, score, totalQuestions);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var quizDetails = await connection.QuerySingleOrDefaultAsync<
                    (int ModuleId, int? PassingScore) > (
                    "SELECT MODULE_ID AS ModuleId, PASSING_SCORE AS PassingScore FROM QUIZZES WHERE QUIZ_ID = @QuizId",
                    new { QuizId = quizId });

                if (quizDetails.ModuleId == 0) 
                {
                    _logger.LogWarning("QuizId {QuizId} not found or has no ModuleId.", quizId);
                    ViewData["ErrorMessage"] = "Quiz not found."; // Pass error to the page
                    return Page(); // Or a more appropriate error handling
                }
                int moduleId = quizDetails.ModuleId;
                int passingScorePercent = quizDetails.PassingScore ?? 70; 

                decimal scorePercent = totalQuestions > 0 ? ((decimal)score / totalQuestions) * 100 : 0;
                bool passed = scorePercent >= passingScorePercent;
                _logger.LogDebug("Quiz {QuizId} attempt by UserId {UserId}: ScorePercent={ScorePercent} (Raw: {Score}/{TotalQuestions}), PassingThreshold={PassingScorePercent}, Passed={Passed}", 
                    quizId, userId, scorePercent, score, totalQuestions, passingScorePercent, passed);

                // Record quiz attempt in QUIZ_ATTEMPTS
                await connection.ExecuteAsync(
                    @"INSERT INTO QUIZ_ATTEMPTS (USER_ID, QUIZ_ID, ATTEMPT_DATE, SCORE, PASSED, ATTEMPT_NUMBER) 
                      VALUES (@UserId, @QuizId, GETDATE(), @Score, @Passed, 
                              ISNULL((SELECT MAX(ISNULL(ATTEMPT_NUMBER,0)) FROM QUIZ_ATTEMPTS WHERE USER_ID = @UserId AND QUIZ_ID = @QuizId), 0) + 1)",
                    new { UserId = userId, QuizId = quizId, Score = score, Passed = passed });
                 _logger.LogDebug("Quiz attempt recorded in QUIZ_ATTEMPTS for QuizId {QuizId}, UserId {UserId}.", quizId, userId);

                 // Update USER_PROGRESS for this specific quiz item
                string quizProgressStatus = passed ? "completed" : "in_progress";
                DateTime? quizCompletedOn = passed ? (DateTime?)DateTime.Now : null;

                await connection.ExecuteAsync(@"
                    MERGE USER_PROGRESS AS target
                    USING (SELECT @UserId AS USER_ID, @ModuleId AS MODULE_ID, @QuizId AS QUIZ_ID) AS source
                    ON (target.USER_ID = source.USER_ID AND target.MODULE_ID = source.MODULE_ID AND target.QUIZ_ID = source.QUIZ_ID)
                    WHEN MATCHED THEN
                        UPDATE SET STATUS = @Status, 
                                   LAST_ACCESSED = GETDATE(), 
                                   COMPLETED_ON = CASE WHEN @Status = 'completed' THEN ISNULL(target.COMPLETED_ON, @CompletedOn) ELSE NULL END,
                                   PERCENT_COMPLETE = @PercentComplete
                    WHEN NOT MATCHED THEN
                        INSERT (USER_ID, MODULE_ID, QUIZ_ID, STATUS, LAST_ACCESSED, COMPLETED_ON, PERCENT_COMPLETE)
                        VALUES (@UserId, @ModuleId, @QuizId, @Status, GETDATE(), @CompletedOn, @PercentComplete);",
                    new { 
                        UserId = userId, 
                        ModuleId = moduleId, 
                        QuizId = quizId, 
                        Status = quizProgressStatus, 
                        CompletedOn = quizCompletedOn,
                        PercentComplete = scorePercent 
                    });
                _logger.LogDebug("USER_PROGRESS updated for QuizId {QuizId}, UserId {UserId} with status: {Status}, percent: {PercentComplete}", quizId, userId, quizProgressStatus, scorePercent);

                if (passed)
                {
                    // If quiz passed, check if the module itself is now complete
                    await CheckModuleCompletion(connection, userId, moduleId);
                }

                // Get course ID to update overall course progress and for redirection
                var courseId = await connection.ExecuteScalarAsync<int>(
                    "SELECT COURSE_ID FROM MODULES WHERE MODULE_ID = @ModuleId",
                    new { ModuleId = moduleId });

                await UpdateCourseProgress(connection, courseId); // Update overall course progress

                int latestAttemptId = await GetLatestAttemptId(connection, userId, quizId);
                return RedirectToPage("/Student/Courses/QuizResult", new { attemptId = latestAttemptId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting quiz attempt for QuizId: {QuizId}, UserId: {UserId}", quizId, userId);
                TempData["ErrorMessage"] = "An error occurred while submitting your quiz attempt. Please try again.";
                return RedirectToPage("/Student/Courses/View", new { id = Course?.CourseId ?? 0 }); // Redirect back to course view with error
            }
        }

        private async Task<int> GetLatestAttemptId(SqlConnection connection, int userId, int quizId)
        {
            return await connection.ExecuteScalarAsync<int>(
                @"SELECT TOP 1 ATTEMPT_ID 
        FROM QUIZ_ATTEMPTS 
        WHERE USER_ID = @UserId AND QUIZ_ID = @QuizId 
        ORDER BY ATTEMPT_DATE DESC",
                new { UserId = userId, QuizId = quizId });
        }

        public async Task<IActionResult> SubmitAssignment(int assignmentId, string submissionText, IFormFile submissionFile = null)
        {
            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
            {
                return RedirectToPage("/Login");
            }
            int userId = BitConverter.ToInt32(userIdBytes, 0);
            _logger.LogInformation("Submitting assignment for AssignmentId: {AssignmentId}, UserId: {UserId}", assignmentId, userId);

            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get Course ID for this assignment for redirection and progress update
                var courseId = await connection.ExecuteScalarAsync<int?>( // Nullable in case assignment somehow doesn't link to a course
                    "SELECT COURSE_ID FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (courseId == null)
                {
                    _logger.LogWarning("AssignmentId {AssignmentId} not found or does not link to a course.", assignmentId);
                    TempData["ErrorMessage"] = "Assignment not found or course link missing.";
                    return RedirectToPage("/Student/Dashboard"); // Or a more appropriate error page
                }

                string fileUrl = null;
                if (submissionFile != null && submissionFile.Length > 0)
                {
                    fileUrl = await SaveSubmissionFile(submissionFile, userId, assignmentId);
                     _logger.LogDebug("File saved for assignment {AssignmentId}, UserId {UserId} at {FileUrl}", assignmentId, userId, fileUrl);
                }

                // Insert or Update submission
                var existingSubmissionId = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT SUBMISSION_ID FROM ASSIGNMENT_SUBMISSIONS WHERE USER_ID = @UserId AND ASSIGNMENT_ID = @AssignmentId",
                        new { UserId = userId, AssignmentId = assignmentId });

                if (existingSubmissionId.HasValue)
                {
                     _logger.LogDebug("Updating existing submission {SubmissionId} for AssignmentId {AssignmentId}", existingSubmissionId.Value, assignmentId);
                    await connection.ExecuteAsync(
                        @"UPDATE ASSIGNMENT_SUBMISSIONS 
                          SET SUBMISSION_TEXT = @SubmissionText, 
                              FILE_URL = ISNULL(@FileUrl, FILE_URL), -- Keep old file if new one is not uploaded
                              SUBMITTED_ON = GETDATE(),
                              GRADE = NULL,      -- Reset grade on re-submission
                              FEEDBACK = NULL,   -- Reset feedback
                              STATUS = 'Submitted', -- Reset status
                              GRADED_ON = NULL
                          WHERE SUBMISSION_ID = @ExistingSubmissionId",
                        new { ExistingSubmissionId = existingSubmissionId.Value, SubmissionText = submissionText, FileUrl = fileUrl });
                }
                else
                {
                    _logger.LogDebug("Inserting new submission for AssignmentId {AssignmentId}", assignmentId);
                    await connection.ExecuteAsync(
                        @"INSERT INTO ASSIGNMENT_SUBMISSIONS (USER_ID, ASSIGNMENT_ID, SUBMISSION_TEXT, FILE_URL, SUBMITTED_ON, STATUS)
                          VALUES (@UserId, @AssignmentId, @SubmissionText, @FileUrl, GETDATE(), 'Submitted')",
                        new { UserId = userId, AssignmentId = assignmentId, SubmissionText = submissionText, FileUrl = fileUrl });
                }

                // No module-specific progress for assignments as they are course-level.
                // Directly update overall course progress.
                await UpdateCourseProgress(connection, courseId.Value);
                _logger.LogInformation("Course progress updated after assignment {AssignmentId} submission by UserId {UserId}.", assignmentId, userId);

                TempData["SuccessMessage"] = "Assignment submitted successfully!";
                return RedirectToPage("/Student/Courses/View", new { id = courseId.Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting assignment {AssignmentId} for UserId {UserId}", assignmentId, userId);
                TempData["ErrorMessage"] = "An error occurred while submitting your assignment.";
                // Attempt to redirect back to the course page if possible, otherwise to dashboard
                var cId = Course?.CourseId;
                if (cId == null && assignmentId > 0) { // Try to get courseId if not already on Course object
                     try { using var conn = new SqlConnection(_connectionString); cId = conn.ExecuteScalar<int?>("SELECT COURSE_ID FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId", new { AssignmentId = assignmentId }); } catch {}
                }
                return RedirectToPage("/Student/Courses/View", new { id = cId ?? 0 }); 
            }
        }

        private async Task<string> SaveSubmissionFile(IFormFile file, int userId, int assignmentId)
        {
            // Implementation depends on your file storage approach
            // This is a placeholder for the actual implementation

            string uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "assignments");
            Directory.CreateDirectory(uploadPath); // Ensure directory exists

            string uniqueFileName = $"user{userId}_assignment{assignmentId}_{DateTime.Now:yyyyMMdd_HHmmss}_{Path.GetFileName(file.FileName)}";
            string filePath = Path.Combine(uploadPath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/assignments/{uniqueFileName}";
        }

        // Model classes
        public class CourseDetails
        {
            public int CourseId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required string ThumbnailUrl { get; set; }
            public required string Instructor { get; set; }
            public required string InstructorEmail { get; set; }
            public DateTime EnrollmentDate { get; set; }
            public decimal Progress { get; set; }
            public required string Status { get; set; }
        }

        public class Module
        {
            public int ModuleId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public int OrderSequence { get; set; }
            public required string CompletionStatus { get; set; }
            public List<Content> Contents { get; set; } = new List<Content>();
        }

        public class Content
        {
            public int ContentId { get; set; }
            public int ModuleId { get; set; }
            public required string ContentTitle { get; set; }
            public required string ContentType { get; set; }
            public required string ItemType { get; set; }
            public int SequenceNumber { get; set; }
            public bool IsCompleted { get; set; }
        }

        public class ContentItem
        {
            public int ModuleId { get; set; }
            public required string ModuleTitle { get; set; }
            public int ItemId { get; set; }
            public required string ItemTitle { get; set; }
            public required string ItemType { get; set; }
        }

        public class CourseAssignment
        {
            public int AssignmentId { get; set; }
            public required string Title { get; set; }
            public required string Instructions { get; set; }
            public DateTime DueDate { get; set; }
            public decimal MaxScore { get; set; }
            public string? ModuleTitle { get; set; }
            public int? ModuleId { get; set; }
            public required string Status { get; set; }
            public decimal? Grade { get; set; }
            public required string Feedback { get; set; }
            public DateTime? SubmittedOn { get; set; }
        }

        public class Announcement
        {
            public int AnnouncementId { get; set; }
            public required string Title { get; set; }
            public required string Content { get; set; }
            public required string PostedBy { get; set; }
            public DateTime PostedDate { get; set; }
        }

        public class Resource
        {
            public int ResourceId { get; set; }
            public required string Title { get; set; }
            public required string Description { get; set; }
            public required string Type { get; set; }
            public required string Url { get; set; }
            public required string ModuleTitle { get; set; }
        }

        public class ModuleCompleteRequest
        {
            public int ModuleId { get; set; }
        }

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostMarkModuleCompleteAsync([FromBody] ModuleCompleteRequest request)
        {
            if (request == null)
                return BadRequest("Request is null");
            if (request.ModuleId == 0)
                return BadRequest("ModuleId is missing or zero");

            if (!HttpContext.Session.TryGetValue("UserId", out var userIdBytes))
                return Unauthorized();

            var userId = BitConverter.ToInt32(userIdBytes, 0);
            var moduleId = request.ModuleId;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Insert or update progress
            await connection.ExecuteAsync(@"
        MERGE USER_MODULE_PROGRESS AS target
        USING (SELECT @UserId AS USER_ID, @ModuleId AS MODULE_ID) AS source
        ON (target.USER_ID = source.USER_ID AND target.MODULE_ID = source.MODULE_ID)
        WHEN MATCHED THEN
            UPDATE SET STATUS = 'completed', COMPLETED_ON = GETDATE()
        WHEN NOT MATCHED THEN
            INSERT (USER_ID, MODULE_ID, STATUS, COMPLETED_ON)
            VALUES (@UserId, @ModuleId, 'completed', GETDATE());",
                new { UserId = userId, ModuleId = moduleId });

            return new JsonResult(new { success = true });
        }
    }
}