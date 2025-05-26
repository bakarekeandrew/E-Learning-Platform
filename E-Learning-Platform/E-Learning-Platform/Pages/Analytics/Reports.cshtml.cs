using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using E_Learning_Platform.Models;
using ClosedXML.Excel;
using System.IO;
using System.Text;
using System.Data;
using E_Learning_Platform.Services;

namespace E_Learning_Platform.Pages.Analytics
{
    [Authorize]
    public class ReportsModel : PageModel
    {
        private readonly ILogger<ReportsModel> _logger;
        private readonly string _connectionString;
        private readonly PdfReportService _pdfService;

        public List<E_Learning_Platform.Models.ReportMetric> AvailableMetrics { get; set; } = new();
        public List<E_Learning_Platform.Models.ReportMetric> SelectedMetrics { get; set; } = new();
        public E_Learning_Platform.Models.ReportData? ReportData { get; set; }

        public ReportsModel(ILogger<ReportsModel> logger, IConfiguration configuration, PdfReportService pdfService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException("Connection string 'DefaultConnection' not found.");
            _pdfService = pdfService ?? throw new ArgumentNullException(nameof(pdfService));
            
            // Initialize metrics
            AvailableMetrics = ReportTypes.AvailableMetrics;
            SelectedMetrics = new List<E_Learning_Platform.Models.ReportMetric>();
        }

        public void OnGet()
        {
            // Initialize the page with default metrics
            SelectedMetrics = AvailableMetrics.Where(m => m.IsDefault).ToList();
        }

        public async Task<IActionResult> OnPostAsync(E_Learning_Platform.Models.ReportFilter filter)
        {
            try
            {
                if (filter.SelectedMetrics == null || !filter.SelectedMetrics.Any())
                {
                    TempData["ErrorMessage"] = "Please select at least one metric.";
                    return RedirectToPage();
                }

                SelectedMetrics = AvailableMetrics
                    .Where(m => filter.SelectedMetrics.Contains(m.Id))
                    .ToList();

                ReportData = await GenerateReportAsync(filter);
                if (ReportData == null)
                {
                    TempData["ErrorMessage"] = "Failed to generate report data.";
                    return RedirectToPage();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                TempData["ErrorMessage"] = "Error generating report. Please try again.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostExportAsync(E_Learning_Platform.Models.ReportFilter filter)
        {
            try
            {
                if (filter.SelectedMetrics == null || !filter.SelectedMetrics.Any())
                {
                    return BadRequest("No metrics selected");
                }

                SelectedMetrics = AvailableMetrics
                    .Where(m => filter.SelectedMetrics.Contains(m.Id))
                    .ToList();

                var reportData = await GenerateReportAsync(filter);
                if (reportData == null)
                {
                    return BadRequest("Failed to generate report data");
                }

                if (string.IsNullOrEmpty(filter.ExportFormat))
                {
                    return BadRequest("Export format not specified");
                }

                IActionResult result = filter.ExportFormat.ToLower() switch
                {
                    "excel" => await ExportToExcelAsync(reportData),
                    "csv" => await Task.FromResult(ExportToCsv(reportData)),
                    "pdf" => await Task.FromResult(ExportToPdf(reportData)),
                    _ => BadRequest("Invalid export format")
                };

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting report");
                return BadRequest("Failed to export report");
            }
        }

        private async Task<E_Learning_Platform.Models.ReportData> GenerateReportAsync(E_Learning_Platform.Models.ReportFilter filter)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var reportData = new E_Learning_Platform.Models.ReportData
            {
                GeneratedAt = DateTime.Now,
                ReportType = filter.ReportType ?? "daily"
            };

            // Build dynamic SQL based on selected metrics
            foreach (var metric in SelectedMetrics)
            {
                object? metricValue = metric.Id switch
                {
                    "daily_active_users" => await GetActiveUsersMetric(connection, filter),
                    "avg_session_duration" => $"{await GetAverageSessionDuration(connection, filter):F1} minutes",
                    "course_completion_rate" => $"{await GetCourseCompletionRate(connection, filter):P1}",
                    "avg_course_rating" => $"{await GetAverageCourseRating(connection, filter):F1} / 5.0",
                    "new_enrollments" => await GetNewEnrollments(connection, filter),
                    "avg_assessment_score" => $"{await GetAverageAssessmentScore(connection, filter):P1}",
                    "module_completion_rate" => $"{await GetModuleCompletionRate(connection, filter):P1}",
                    _ => null
                };

                if (metricValue != null)
                {
                    reportData.Metrics[metric.Name] = metricValue;
                }
            }

            // Get time series data based on report type
            reportData.Data = await GetTimeSeriesData(connection, filter) ?? new List<E_Learning_Platform.Models.ReportRow>();

            return reportData;
        }

        private async Task<int> GetActiveUsersMetric(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            var sql = @"
                SELECT COUNT(DISTINCT cp.USER_ID)
                FROM COURSE_PROGRESS cp
                JOIN USERS u ON cp.USER_ID = u.USER_ID
                WHERE cp.LAST_ACCESSED BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1";

            if (!string.IsNullOrEmpty(filter.UserRole))
            {
                sql += " AND u.ROLE_ID = (SELECT ROLE_ID FROM ROLES WHERE ROLE_NAME = @UserRole)";
            }

            return await connection.ExecuteScalarAsync<int>(sql, filter);
        }

        private async Task<double> GetAverageSessionDuration(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            return await connection.ExecuteScalarAsync<double>(@"
                WITH SessionDurations AS (
                    SELECT 
                        USER_ID,
                        DATEDIFF(minute, 
                            LAG(LAST_ACCESSED) OVER (PARTITION BY USER_ID ORDER BY LAST_ACCESSED), 
                            LAST_ACCESSED) as Duration
                    FROM COURSE_PROGRESS
                    WHERE LAST_ACCESSED BETWEEN @StartDate AND @EndDate
                )
                SELECT ISNULL(AVG(CASE 
                    WHEN Duration > 0 AND Duration <= 240 THEN Duration
                    ELSE 30
                END), 30)
                FROM SessionDurations
                WHERE Duration IS NOT NULL", filter);
        }

        private async Task<double> GetCourseCompletionRate(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            var result = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                SELECT 
                    SUM(CASE WHEN STATUS = 'Completed' THEN 1 ELSE 0 END) as completed,
                    COUNT(*) as total
                FROM COURSE_ENROLLMENTS ce
                JOIN USERS u ON ce.USER_ID = u.USER_ID
                WHERE ce.ENROLLMENT_DATE BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1", filter);

            return result.total > 0 ? (double)result.completed / result.total : 0;
        }

        private async Task<double> GetAverageCourseRating(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            return await connection.ExecuteScalarAsync<double>(@"
                SELECT ISNULL(AVG(CAST(RATING AS FLOAT)), 0)
                FROM REVIEWS r
                JOIN USERS u ON r.USER_ID = u.USER_ID
                WHERE r.REVIEW_DATE BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1", filter);
        }

        private async Task<int> GetNewEnrollments(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            return await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*)
                FROM COURSE_ENROLLMENTS ce
                JOIN USERS u ON ce.USER_ID = u.USER_ID
                WHERE ce.ENROLLMENT_DATE BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1", filter);
        }

        private async Task<double> GetAverageAssessmentScore(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            var result = await connection.QueryFirstOrDefaultAsync<(int totalScore, int count)>(@"
                SELECT 
                    SUM(SCORE) as totalScore,
                    COUNT(*) as count
                FROM ASSESSMENT_RESULTS ar
                JOIN USERS u ON ar.USER_ID = u.USER_ID
                WHERE ar.COMPLETION_DATE BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1", filter);

            return result.count > 0 ? (double)result.totalScore / (result.count * 100) : 0;
        }

        private async Task<double> GetModuleCompletionRate(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            var result = await connection.QueryFirstOrDefaultAsync<(int completed, int total)>(@"
                SELECT 
                    SUM(CASE WHEN COMPLETION_STATUS = 'Completed' THEN 1 ELSE 0 END) as completed,
                    COUNT(*) as total
                FROM MODULE_PROGRESS mp
                JOIN USERS u ON mp.USER_ID = u.USER_ID
                WHERE mp.LAST_ACCESSED BETWEEN @StartDate AND @EndDate
                AND u.IS_ACTIVE = 1", filter);

            return result.total > 0 ? (double)result.completed / result.total : 0;
        }

        private async Task<List<E_Learning_Platform.Models.ReportRow>> GetTimeSeriesData(SqlConnection connection, E_Learning_Platform.Models.ReportFilter filter)
        {
            var data = new List<E_Learning_Platform.Models.ReportRow>();
            var dateFormat = filter.ReportType switch
            {
                "weekly" => "DATEADD(day, -DATEPART(weekday, LAST_ACCESSED) + 1, CAST(LAST_ACCESSED AS DATE))",
                "monthly" => "DATEFROMPARTS(YEAR(LAST_ACCESSED), MONTH(LAST_ACCESSED), 1)",
                _ => "CAST(LAST_ACCESSED AS DATE)" // daily
            };

            foreach (var metric in SelectedMetrics)
            {
                var sql = $@"
                    SELECT 
                        {dateFormat} as Date,
                        COUNT(DISTINCT USER_ID) as Value
                    FROM COURSE_PROGRESS
                    WHERE LAST_ACCESSED BETWEEN @StartDate AND @EndDate
                    GROUP BY {dateFormat}
                    ORDER BY Date";

                var results = await connection.QueryAsync<(DateTime Date, int Value)>(sql, filter);

                foreach (var result in results)
                {
                    var row = data.FirstOrDefault(r => r.Date == result.Date) ?? new E_Learning_Platform.Models.ReportRow
                    {
                        Date = result.Date,
                        Category = metric.Category
                    };

                    row.Values[metric.Id] = result.Value;

                    if (!data.Contains(row))
                    {
                        data.Add(row);
                    }
                }
            }

            return data.OrderBy(r => r.Date).ToList();
        }

        private async Task<IActionResult> ExportToExcelAsync(E_Learning_Platform.Models.ReportData reportData)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Analytics Report");

            // Add title
            worksheet.Cell("A1").Value = $"Analytics Report - {reportData.ReportType}";
            worksheet.Range("A1:D1").Merge();

            // Add summary
            var row = 3;
            worksheet.Cell(row, 1).Value = "Summary";
            row++;
            foreach (var metric in reportData.Metrics)
            {
                worksheet.Cell(row, 1).Value = metric.Key;
                worksheet.Cell(row, 2).Value = ConvertToXLCellValue(metric.Value);
                row++;
            }

            // Add detailed data
            row += 2;
            var col = 1;
            worksheet.Cell(row, col++).Value = "Date";
            worksheet.Cell(row, col++).Value = "Category";
            foreach (var metric in SelectedMetrics)
            {
                worksheet.Cell(row, col++).Value = metric.Name;
            }

            foreach (var dataRow in reportData.Data)
            {
                row++;
                col = 1;
                worksheet.Cell(row, col++).Value = dataRow.Date;
                worksheet.Cell(row, col++).Value = dataRow.Category;
                foreach (var metric in SelectedMetrics)
                {
                    var value = dataRow.Values.ContainsKey(metric.Id)
                        ? ConvertToXLCellValue(dataRow.Values[metric.Id])
                        : "-";
                    worksheet.Cell(row, col++).Value = value;
                }
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            ms.Position = 0;
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "analytics_report.xlsx");
        }

        private static string ConvertToXLCellValue(object value)
        {
            if (value == null)
                return "-";

            // Handle specific types that need formatting
            return value switch
            {
                DateTime dateTime => dateTime.ToString("g"),
                double doubleValue => doubleValue.ToString("F2"),
                decimal decimalValue => decimalValue.ToString("F2"),
                _ => value.ToString()
            };
        }

        private IActionResult ExportToCsv(E_Learning_Platform.Models.ReportData reportData)
        {
            var csv = new StringBuilder();

            // Add summary
            csv.AppendLine("Summary");
            foreach (var metric in reportData.Metrics)
            {
                csv.AppendLine($"{metric.Key},{metric.Value}");
            }
            csv.AppendLine();

            // Add headers
            csv.Append("Date,Category");
            foreach (var metric in SelectedMetrics)
            {
                csv.Append($",{metric.Name}");
            }
            csv.AppendLine();

            // Add data
            foreach (var row in reportData.Data)
            {
                csv.Append($"{row.Date:d},{row.Category}");
                foreach (var metric in SelectedMetrics)
                {
                    var value = row.Values.ContainsKey(metric.Id) ? row.Values[metric.Id] : "-";
                    csv.Append($",{value}");
                }
                csv.AppendLine();
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "analytics_report.csv");
        }

        private IActionResult ExportToPdf(E_Learning_Platform.Models.ReportData reportData)
        {
            var pdfBytes = _pdfService.GenerateAnalyticsReport(reportData, SelectedMetrics);
            return File(pdfBytes, "application/pdf", "analytics_report.pdf");
        }
    }

    public class ReportFilter
    {
        public required string ReportType { get; set; }
        public required List<string> SelectedMetrics { get; set; }
        public string? ExportFormat { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? UserRole { get; set; }
    }

    public class ReportMetric
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Category { get; set; }
        public bool IsDefault { get; set; }
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
} 