using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using System.Linq;

namespace E_Learning_Platform.Services
{
    public class AssignmentService : IAssignmentService
    {
        private readonly string _connectionString;
        private readonly ILogger<AssignmentService> _logger;
        private readonly INotificationEventService _notificationEventService;

        public AssignmentService(
            IConfiguration configuration,
            ILogger<AssignmentService> logger,
            INotificationEventService notificationEventService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
            _notificationEventService = notificationEventService ??
                throw new ArgumentNullException(nameof(notificationEventService));
        }

        private void NotifyStudents(IEnumerable<int> studentIds, string title, string message, string type)
        {
            if (studentIds == null) return;

            foreach (var studentId in studentIds)
            {
                _notificationEventService.RaiseEvent(new NotificationEvent
                {
                    UserId = studentId,
                    Title = title,
                    Message = message,
                    Type = type
                });
            }
        }

        public async Task<int> CreateAssignmentAsync(int courseId, string title, string instructions, DateTime dueDate, int maxScore, int createdBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get enrolled students - Fix: Remove .AsList() and use ToList() instead
                var enrolledStudents = await connection.QueryAsync<int>(
                    "SELECT CAST(USER_ID as int) FROM COURSE_ENROLLMENTS WHERE COURSE_ID = @CourseId",
                    new { CourseId = courseId });

                // Get course title for notification
                var courseTitle = await connection.QuerySingleAsync<string>(
                    "SELECT TITLE FROM COURSES WHERE COURSE_ID = @CourseId",
                    new { CourseId = courseId });

                // Create assignment
                var assignmentId = await connection.QuerySingleAsync<int>(@"
                    INSERT INTO ASSIGNMENTS (
                        COURSE_ID, TITLE, INSTRUCTIONS, DUE_DATE, MAX_SCORE, CREATED_BY, CREATED_AT
                    ) VALUES (
                        @CourseId, @Title, @Instructions, @DueDate, @MaxScore, @CreatedBy, GETDATE()
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int);",
                    new
                    {
                        CourseId = courseId,
                        Title = title,
                        Instructions = instructions,
                        DueDate = dueDate,
                        MaxScore = maxScore,
                        CreatedBy = createdBy
                    });

                // Notify enrolled students
                NotifyStudents(
                    enrolledStudents,
                    "New Assignment",
                    $"A new assignment '{title}' has been added to the course '{courseTitle}'. Due date: {dueDate:MMM dd, yyyy}",
                    "info"
                );

                return assignmentId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating assignment for course {CourseId}", courseId);
                throw;
            }
        }

        public async Task<bool> UpdateAssignmentAsync(int assignmentId, string title, string instructions, DateTime dueDate, int maxScore, int updatedBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get old assignment details for comparison
                var oldAssignment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (oldAssignment == null) return false;

                // Update the assignment
                var updated = await connection.ExecuteAsync(@"
                    UPDATE ASSIGNMENTS 
                    SET TITLE = @Title, 
                        INSTRUCTIONS = @Instructions, 
                        DUE_DATE = @DueDate, 
                        MAX_SCORE = @MaxScore
                    WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId, Title = title, Instructions = instructions, DueDate = dueDate, MaxScore = maxScore });

                if (updated > 0)
                {
                    // Get enrolled students - Fix: Remove .AsList()
                    var enrolledStudents = await connection.QueryAsync<int>(@"
                        SELECT CAST(ce.USER_ID as int)
                        FROM COURSE_ENROLLMENTS ce
                        JOIN ASSIGNMENTS a ON ce.COURSE_ID = a.COURSE_ID
                        WHERE a.ASSIGNMENT_ID = @AssignmentId",
                        new { AssignmentId = assignmentId });

                    // Notify students about the changes
                    var changes = new List<string>();
                    if (title != oldAssignment.TITLE) changes.Add("title");
                    if (instructions != oldAssignment.INSTRUCTIONS) changes.Add("instructions");
                    if (dueDate != oldAssignment.DUE_DATE) changes.Add("due date");
                    if (maxScore != oldAssignment.MAX_SCORE) changes.Add("maximum score");

                    if (changes.Any())
                    {
                        NotifyStudents(
                            enrolledStudents,
                            "Assignment Updated",
                            $"The assignment '{title}' in course '{oldAssignment.CourseTitle}' has been updated. Changes: {string.Join(", ", changes)}.",
                            "info"
                        );
                    }
                }

                return updated > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating assignment {AssignmentId}", assignmentId);
                throw;
            }
        }

        public async Task<bool> DeleteAssignmentAsync(int assignmentId, int deletedBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment details before deletion
                var assignment = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignment == null) return false;

                // Get enrolled students before deletion - Fix: Remove .AsList()
                var enrolledStudents = await connection.QueryAsync<int>(@"
                    SELECT CAST(ce.USER_ID as int)
                    FROM COURSE_ENROLLMENTS ce
                    JOIN ASSIGNMENTS a ON ce.COURSE_ID = a.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                // Delete the assignment
                var deleted = await connection.ExecuteAsync(
                    "DELETE FROM ASSIGNMENTS WHERE ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (deleted > 0)
                {
                    // Notify students about the deletion
                    NotifyStudents(
                        enrolledStudents,
                        "Assignment Removed",
                        $"The assignment '{assignment.TITLE}' from course '{assignment.CourseTitle}' has been removed.",
                        "warning"
                    );
                }

                return deleted > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting assignment {AssignmentId}", assignmentId);
                throw;
            }
        }

        public async Task<bool> SubmitAssignmentAsync(int assignmentId, int userId, string submissionText, string fileUrl)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment and course details
                var assignmentDetails = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle, CAST(c.CREATED_BY as int) as InstructorId
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignmentDetails == null) return false;

                // Submit the assignment
                var submissionId = await connection.QuerySingleAsync<int>(@"
                    INSERT INTO ASSIGNMENT_SUBMISSIONS (ASSIGNMENT_ID, USER_ID, SUBMISSION_TEXT, FILE_URL, SUBMITTED_ON, STATUS)
                    OUTPUT INSERTED.SUBMISSION_ID
                    VALUES (@AssignmentId, @UserId, @SubmissionText, @FileUrl, GETDATE(), 'Submitted')",
                    new { AssignmentId = assignmentId, UserId = userId, SubmissionText = submissionText, FileUrl = fileUrl });

                if (submissionId > 0)
                {
                    // Get student name
                    var studentName = await connection.QuerySingleAsync<string>(
                        "SELECT FULL_NAME FROM USERS WHERE USER_ID = @UserId",
                        new { UserId = userId });

                    // Notify instructor about the submission - Fix: Create proper int array
                    NotifyStudents(
                        new int[] { assignmentDetails.InstructorId },
                        "New Assignment Submission",
                        $"Student {studentName} has submitted the assignment '{assignmentDetails.TITLE}' for course '{assignmentDetails.CourseTitle}'.",
                        "info"
                    );
                }

                return submissionId > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting assignment {AssignmentId} for user {UserId}", assignmentId, userId);
                throw;
            }
        }

        public async Task<bool> GradeAssignmentAsync(int assignmentId, int studentId, decimal grade, string feedback, int gradedBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment details
                var assignmentDetails = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignmentDetails == null) return false;

                // Update the grade
                var updated = await connection.ExecuteAsync(@"
                    UPDATE ASSIGNMENT_SUBMISSIONS
                    SET GRADE = @Grade, 
                        FEEDBACK = @Feedback,
                        STATUS = 'Graded'
                    WHERE ASSIGNMENT_ID = @AssignmentId 
                    AND USER_ID = @StudentId",
                    new { AssignmentId = assignmentId, StudentId = studentId, Grade = grade, Feedback = feedback });

                if (updated > 0)
                {
                    // Notify student about the grade - Fix: Create proper int array
                    NotifyStudents(
                        new int[] { studentId },
                        "Assignment Graded",
                        $"Your assignment '{assignmentDetails.TITLE}' for course '{assignmentDetails.CourseTitle}' has been graded. Grade: {grade}",
                        "success"
                    );
                }

                return updated > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error grading assignment {AssignmentId} for student {StudentId}", assignmentId, studentId);
                throw;
            }
        }

        public async Task<bool> AssignToStudentAsync(int assignmentId, int studentId, int assignedBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment details
                var assignmentDetails = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignmentDetails == null) return false;

                // Enroll student in the course if not already enrolled
                await connection.ExecuteAsync(@"
                    IF NOT EXISTS (
                        SELECT 1 FROM COURSE_ENROLLMENTS 
                        WHERE USER_ID = @StudentId AND COURSE_ID = @CourseId
                    )
                    INSERT INTO COURSE_ENROLLMENTS (USER_ID, COURSE_ID, ENROLLMENT_DATE)
                    VALUES (@StudentId, @CourseId, GETDATE())",
                    new { StudentId = studentId, CourseId = assignmentDetails.COURSE_ID });

                // Notify student about the new assignment - Fix: Create proper int array
                NotifyStudents(
                    new int[] { studentId },
                    "New Assignment",
                    $"You have been assigned a new task: '{assignmentDetails.TITLE}' in course '{assignmentDetails.CourseTitle}'. Due date: {assignmentDetails.DUE_DATE:MMM dd, yyyy}",
                    "info"
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning assignment {AssignmentId} to student {StudentId}", assignmentId, studentId);
                throw;
            }
        }

        public async Task<bool> RemoveFromStudentAsync(int assignmentId, int studentId, int removedBy)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Get assignment details
                var assignmentDetails = await connection.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT a.*, c.TITLE as CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE a.ASSIGNMENT_ID = @AssignmentId",
                    new { AssignmentId = assignmentId });

                if (assignmentDetails == null) return false;

                // Remove any existing submissions
                var removed = await connection.ExecuteAsync(
                    "DELETE FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = @AssignmentId AND USER_ID = @StudentId",
                    new { AssignmentId = assignmentId, StudentId = studentId });

                // Notify student about the removal - Fix: Create proper int array
                NotifyStudents(
                    new int[] { studentId },
                    "Assignment Removed",
                    $"The assignment '{assignmentDetails.TITLE}' from course '{assignmentDetails.CourseTitle}' has been removed from your tasks.",
                    "warning"
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing assignment {AssignmentId} from student {StudentId}", assignmentId, studentId);
                throw;
            }
        }

        public async Task<IEnumerable<dynamic>> GetStudentAssignmentsAsync(int studentId, string filter = "all")
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        a.COURSE_ID AS CourseId,
                        c.TITLE AS CourseTitle,
                        s.SUBMISSION_ID AS SubmissionId,
                        s.SUBMITTED_ON AS SubmittedOn,
                        s.GRADE AS Grade,
                        s.FEEDBACK AS Feedback,
                        s.STATUS AS Status
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID AND ce.USER_ID = @StudentId
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND s.USER_ID = @StudentId";

                switch (filter.ToLower())
                {
                    case "pending":
                        query += " WHERE s.SUBMISSION_ID IS NULL";
                        break;
                    case "submitted":
                        query += " WHERE s.SUBMISSION_ID IS NOT NULL";
                        break;
                    case "graded":
                        query += " WHERE s.GRADE IS NOT NULL";
                        break;
                    case "upcoming":
                        query += " WHERE a.DUE_DATE > GETDATE() AND a.DUE_DATE <= DATEADD(day, 7, GETDATE()) AND s.SUBMISSION_ID IS NULL";
                        break;
                    case "overdue":
                        query += " WHERE a.DUE_DATE < GETDATE() AND s.SUBMISSION_ID IS NULL";
                        break;
                }

                query += " ORDER BY a.DUE_DATE ASC";

                // Fix: Remove .AsList() - return IEnumerable<dynamic> directly
                return await connection.QueryAsync<dynamic>(query, new { StudentId = studentId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assignments for student {StudentId}", studentId);
                throw;
            }
        }

        public async Task<IEnumerable<dynamic>> GetInstructorAssignmentsAsync(int instructorId, int? courseId = null)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);

                var query = @"
                    SELECT 
                        a.ASSIGNMENT_ID AS AssignmentId,
                        a.TITLE AS Title,
                        a.INSTRUCTIONS AS Instructions,
                        a.DUE_DATE AS DueDate,
                        a.MAX_SCORE AS MaxScore,
                        (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = a.ASSIGNMENT_ID) AS SubmissionCount,
                        (SELECT COUNT(*) FROM ASSIGNMENT_SUBMISSIONS WHERE ASSIGNMENT_ID = a.ASSIGNMENT_ID AND GRADE IS NULL) AS UngradedCount,
                        c.TITLE AS CourseTitle
                    FROM ASSIGNMENTS a
                    JOIN COURSES c ON a.COURSE_ID = c.COURSE_ID
                    WHERE c.CREATED_BY = @InstructorId";

                if (courseId.HasValue)
                {
                    query += " AND a.COURSE_ID = @CourseId";
                }

                query += " ORDER BY a.DUE_DATE";

                // Fix: Remove .AsList() - return IEnumerable<dynamic> directly
                return await connection.QueryAsync<dynamic>(query, new { InstructorId = instructorId, CourseId = courseId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assignments for instructor {InstructorId}", instructorId);
                throw;
            }
        }
    }
}