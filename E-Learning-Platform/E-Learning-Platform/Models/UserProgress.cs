using System;

namespace E_Learning_Platform.Models
{
    public class UserProgress
    {
        public int ProgressId { get; set; }
        public string UserId { get; set; }
        public int ModuleId { get; set; }
        public string ModuleTitle { get; set; }
        public string Status { get; set; }
        public DateTime? LastAccessed { get; set; }
        public DateTime? CompletedOn { get; set; }
    }
} 