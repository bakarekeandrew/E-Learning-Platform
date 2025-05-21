namespace E_Learning_Platform.Models
{
    public class CourseProgress
    {
        public int ProgressId { get; set; }
        public int EnrollmentId { get; set; }
        public int CompletedLessons { get; set; }
        public int TotalLessons { get; set; }
    }
} 