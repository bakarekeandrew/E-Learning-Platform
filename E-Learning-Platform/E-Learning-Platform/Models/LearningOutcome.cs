namespace E_Learning_Platform.Models
{
    public class LearningOutcome
    {
        public int LearningOutcomeId { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; }
        public string Description { get; set; }
        public int Order { get; set; }
    }
} 