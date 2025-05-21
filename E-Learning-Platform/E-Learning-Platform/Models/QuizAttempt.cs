using System;

namespace E_Learning_Platform.Models
{
    public class QuizAttempt
    {
        public int AttemptId { get; set; }
        public string UserId { get; set; }
        public int QuizId { get; set; }
        public string QuizTitle { get; set; }
        public int AttemptNumber { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal? Score { get; set; }
        public bool? Passed { get; set; }
    }
} 