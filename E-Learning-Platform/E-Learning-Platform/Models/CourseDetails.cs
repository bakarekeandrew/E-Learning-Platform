using System;

namespace E_Learning_Platform.Models
{
    public class CourseDetails
    {
        public CourseDetails()
        {
            CourseTitle = string.Empty;
            Category = string.Empty;
            CreatedBy = string.Empty;
            Description = string.Empty;
            InstructorName = string.Empty;
        }

        public string CourseTitle { get; set; }
        public string Category { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string Description { get; set; }
        public string InstructorName { get; set; }
        public int EnrollmentCount { get; set; }
        public double CompletionRate { get; set; }
    }
} 