using System;

namespace E_Learning_Platform.Models
{
    public class AssignmentSubmission
    {
        public int SubmissionId { get; set; }
        public string UserId { get; set; }
        public int AssignmentId { get; set; }
        public string SubmissionText { get; set; }
        public string FileUrl { get; set; }
        public DateTime SubmittedOn { get; set; }
        public decimal? Grade { get; set; }
        public string Feedback { get; set; }
        public string Status { get; set; }
        public string AssignmentTitle { get; set; }
    }
} 