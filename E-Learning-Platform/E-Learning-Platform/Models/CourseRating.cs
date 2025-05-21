using System;

namespace E_Learning_Platform.Models
{
    public class CourseRating
    {
        public int RatingId { get; set; }
        public int CourseId { get; set; }
        public Course Course { get; set; }
        public int UserId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
        public DateTime RatingDate { get; set; }
    }
} 