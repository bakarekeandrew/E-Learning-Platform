using System;

namespace E_Learning_Platform.Models
{
    public class UserActivity
    {
        public int ActivityId { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
        public DateTime ActivityDate { get; set; }
        public string ActivityType { get; set; }
        public string Description { get; set; }
    }
} 