using System;

namespace E_Learning_Platform.Models
{
    public class CourseRating
    {
        public int ReviewId { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; }
        public int UserId { get; set; }
        public decimal Rating { get; set; }
        public string ReviewText { get; set; }
        public DateTime ReviewDate { get; set; }
    }
} 