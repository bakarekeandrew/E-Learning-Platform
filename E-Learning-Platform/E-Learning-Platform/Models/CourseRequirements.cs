using System;

namespace E_Learning_Platform.Models
{
    public class CourseRequirements
    {
        public int TotalModules { get; set; }
        public int CompletedModules { get; set; }
        public decimal? AverageGrade { get; set; }
        public int PassedQuizzes { get; set; }
    }
} 