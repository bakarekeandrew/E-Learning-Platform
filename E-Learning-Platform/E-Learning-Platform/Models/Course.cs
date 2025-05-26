using System;
using System.Collections.Generic;

namespace E_Learning_Platform.Models
{
    public class Course
    {
        public int CourseId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int InstructorId { get; set; }
        public User Instructor { get; set; }
        public decimal Price { get; set; }
        public bool IsPublished { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public List<Enrollment> Enrollments { get; set; }
        public List<LearningOutcome> LearningOutcomes { get; set; }
    }
} 