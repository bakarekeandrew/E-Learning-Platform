using System;

namespace E_Learning_Platform.Models
{
    public class UserSession
    {
        public int SessionId { get; set; }
        public int UserId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
} 