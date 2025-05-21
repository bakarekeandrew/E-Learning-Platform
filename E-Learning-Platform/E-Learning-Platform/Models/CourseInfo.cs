using System;

namespace E_Learning_Platform.Models
{
    public class CourseInfo
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Instructor { get; set; }
        public string ThumbnailUrl { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Category { get; set; }
        public decimal Progress { get; set; }
        public string Status { get; set; }
    }
} 