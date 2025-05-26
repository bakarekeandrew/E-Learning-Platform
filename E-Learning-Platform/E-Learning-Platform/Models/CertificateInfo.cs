using System;

namespace E_Learning_Platform.Models
{
    public class CertificateInfo
    {
        public int CertificateId { get; set; }
        public int UserId { get; set; }
        public string CertificateUrl { get; set; } = string.Empty;
        public string VerificationCode { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime CompletionDate { get; set; }
    }
} 