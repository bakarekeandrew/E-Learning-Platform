using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace E_Learning_Platform.Models
{
    public class ReportFilter
    {
        public required string ReportType { get; set; }
        public required List<string> SelectedMetrics { get; set; }
        public string? ExportFormat { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? UserRole { get; set; }
    }

    public class ReportData
    {
        public DateTime GeneratedAt { get; set; }
        public required string ReportType { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
        public List<ReportRow> Data { get; set; } = new();
    }

    public class ReportRow
    {
        public DateTime Date { get; set; }
        public required string Category { get; set; }
        public Dictionary<string, object> Values { get; set; } = new();
    }

    public class ReportMetric
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Category { get; set; }
        public required string Description { get; set; }
        public bool IsDefault { get; set; }

        public ReportMetric()
        {
            Id = string.Empty;
            Name = string.Empty;
            Category = string.Empty;
            Description = string.Empty;
        }
    }

    public static class ReportTypes
    {
        public static readonly List<ReportMetric> AvailableMetrics = new()
        {
            // User Engagement Metrics
            new ReportMetric { 
                Id = "daily_active_users", 
                Name = "Daily Active Users",
                Category = "User Engagement",
                Description = "Number of unique users who accessed the platform",
                IsDefault = true
            },
            new ReportMetric { 
                Id = "avg_session_duration", 
                Name = "Average Session Duration",
                Category = "User Engagement",
                Description = "Average time users spend on the platform per session",
                IsDefault = true
            },
            new ReportMetric { 
                Id = "retention_rate", 
                Name = "Retention Rate",
                Category = "User Engagement",
                Description = "Percentage of users who return to the platform",
                IsDefault = false
            },

            // Course Performance Metrics
            new ReportMetric { 
                Id = "course_completion_rate", 
                Name = "Course Completion Rate",
                Category = "Course Performance",
                Description = "Percentage of enrolled users who completed their courses",
                IsDefault = true
            },
            new ReportMetric { 
                Id = "avg_course_rating", 
                Name = "Average Course Rating",
                Category = "Course Performance",
                Description = "Average rating given to courses",
                IsDefault = true
            },
            new ReportMetric { 
                Id = "new_enrollments", 
                Name = "New Enrollments",
                Category = "Course Performance",
                Description = "Number of new course enrollments",
                IsDefault = false
            },

            // Learning Progress Metrics
            new ReportMetric { 
                Id = "avg_assessment_score", 
                Name = "Average Assessment Score",
                Category = "Learning Progress",
                Description = "Average score achieved in assessments",
                IsDefault = true
            },
            new ReportMetric { 
                Id = "module_completion_rate", 
                Name = "Module Completion Rate",
                Category = "Learning Progress",
                Description = "Percentage of modules completed by users",
                IsDefault = false
            }
        };
    }
} 