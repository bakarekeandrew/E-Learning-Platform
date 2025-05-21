using System;

namespace E_Learning_Platform.Models
{
    public class CourseDetails
    {
        public string CourseTitle { get; set; }
        public string Category { get; set; }
        public DateTime CreatedOn { get; set; }
        public string CreatedBy { get; set; }
        public string Description { get; set; }
        public decimal Progress { get; set; }
    }
} 