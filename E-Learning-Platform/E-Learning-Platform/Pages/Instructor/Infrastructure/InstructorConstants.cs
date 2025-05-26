using System;

namespace E_Learning_Platform.Pages.Instructor.Infrastructure
{
    public static class InstructorConstants
    {
        public static class SessionKeys
        {
            public const string InstructorId = "UserId";
        }

        public static class QueryConstants
        {
            public const string GetInstructorCourses = @"
                SELECT 
                    c.COURSE_ID AS CourseId,
                    c.TITLE AS Title,
                    c.DESCRIPTION AS Description,
                    COUNT(DISTINCT ce.ENROLLMENT_ID) AS StudentCount,
                    COALESCE(AVG(CAST(r.RATING AS DECIMAL(3,2))), 0) AS Rating,
                    c.IS_ACTIVE AS IsActive,
                    c.CREATION_DATE AS CreatedDate
                FROM COURSES c
                LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                LEFT JOIN REVIEWS r ON c.COURSE_ID = r.COURSE_ID
                WHERE c.CREATED_BY = @InstructorId
                GROUP BY 
                    c.COURSE_ID, 
                    c.TITLE, 
                    c.DESCRIPTION,
                    c.IS_ACTIVE,
                    c.CREATION_DATE
                ORDER BY c.CREATION_DATE DESC";

            public const string GetInstructorModules = @"
                SELECT 
                    m.MODULE_ID AS ModuleId,
                    m.TITLE AS Title,
                    m.DESCRIPTION AS Description,
                    m.SEQUENCE_NUMBER AS SequenceNumber,
                    m.IS_FREE AS IsFree,
                    m.DURATION_MINUTES AS DurationMinutes,
                    c.COURSE_ID AS CourseId,
                    c.TITLE AS CourseTitle
                FROM MODULES m
                JOIN COURSES c ON m.COURSE_ID = c.COURSE_ID
                WHERE c.CREATED_BY = @InstructorId
                ORDER BY m.SEQUENCE_NUMBER";

            public const string GetInstructorQuizzes = @"
                SELECT 
                    Q.QUIZ_ID AS QuizId,
                    Q.TITLE AS Title,
                    Q.DESCRIPTION AS Description,
                    Q.TIME_LIMIT_MINUTES AS TimeLimitMinutes,
                    Q.PASSING_SCORE AS PassingScore,
                    Q.IS_ACTIVE AS IsActive,
                    COUNT(QQ.QUESTION_ID) AS QuestionCount
                FROM QUIZZES Q
                LEFT JOIN QUIZ_QUESTIONS QQ ON Q.QUIZ_ID = QQ.QUIZ_ID
                JOIN MODULES M ON Q.MODULE_ID = M.MODULE_ID
                JOIN COURSES C ON M.COURSE_ID = C.COURSE_ID
                WHERE C.CREATED_BY = @InstructorId
                GROUP BY 
                    Q.QUIZ_ID,
                    Q.TITLE,
                    Q.DESCRIPTION,
                    Q.TIME_LIMIT_MINUTES,
                    Q.PASSING_SCORE,
                    Q.IS_ACTIVE";
        }
    }
} 