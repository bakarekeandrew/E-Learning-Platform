using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;
//using iTextSharp.text;
//using iTextSharp.text.pdf;
using Dapper;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace E_Learning_Platform.Pages
{
    public class ReportsModel : PageModel
    {
        private readonly string _connectionString;

        public ReportsModel(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IActionResult> OnPostGenerateReportAsync([FromForm] string type, [FromForm] string format)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var reportData = type switch
                {
                    "user-engagement" => await GenerateUserEngagementReport(connection),
                    "course-performance" => await GenerateCoursePerformanceReport(connection),
                    "learning-progress" => await GenerateLearningProgressReport(connection),
                    "certification-stats" => await GenerateCertificationStatsReport(connection),
                    _ => throw new ArgumentException("Invalid report type")
                };

                if (reportData == null || !reportData.Any())
                {
                    return BadRequest("No data available for the selected report type.");
                }

                var fileName = $"{type}_{DateTime.Now:yyyyMMdd}";
                
                switch (format.ToLower())
                {
                    case "excel":
                        var excelBytes = GenerateExcelReport(reportData, type);
                        return File(
                            excelBytes,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"{fileName}.xlsx"
                        );

                    //case "pdf":
                    //    var pdfBytes = GeneratePdfReport(reportData, type);
                    //    return File(
                    //        pdfBytes,
                    //        "application/pdf",
                    //        $"{fileName}.pdf"
                    //    );

                    case "csv":
                        var csvBytes = GenerateCsvReport(reportData);
                        return File(
                            csvBytes,
                            "text/csv",
                            $"{fileName}.csv"
                        );

                    default:
                        return BadRequest("Invalid format specified.");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating report: {ex.Message}");
            }
        }

        private async Task<List<Dictionary<string, object>>> GenerateUserEngagementReport(SqlConnection connection)
        {
            // Optimize query with indexes and better joins
            var results = await connection.QueryAsync(@"
                WITH UserMetrics AS (
                    SELECT 
                        u.USER_ID,
                        u.FULL_NAME,
                        u.EMAIL,
                        COUNT(DISTINCT ce.COURSE_ID) as EnrolledCourses,
                        MAX(ce.ENROLLMENT_DATE) as LastEnrollmentDate
                    FROM USERS u
                    LEFT JOIN COURSE_ENROLLMENTS ce ON u.USER_ID = ce.USER_ID
                    GROUP BY u.USER_ID, u.FULL_NAME, u.EMAIL
                ),
                ProgressMetrics AS (
                    SELECT 
                        cp.USER_ID,
                        AVG(CAST(cp.PROGRESS as FLOAT)) as AverageProgress
                    FROM COURSE_PROGRESS cp
                    GROUP BY cp.USER_ID
                ),
                SubmissionMetrics AS (
                    SELECT 
                        s.USER_ID,
                        COUNT(DISTINCT s.SUBMISSION_ID) as AssignmentSubmissions,
                        COUNT(DISTINCT CASE WHEN s.GRADE IS NOT NULL THEN s.SUBMISSION_ID END) as CompletedAssignments
                    FROM ASSIGNMENT_SUBMISSIONS s
                    GROUP BY s.USER_ID
                )
                SELECT 
                    um.*,
                    ISNULL(pm.AverageProgress, 0) as AverageProgress,
                    ISNULL(sm.AssignmentSubmissions, 0) as AssignmentSubmissions,
                    ISNULL(sm.CompletedAssignments, 0) as CompletedAssignments
                FROM UserMetrics um
                LEFT JOIN ProgressMetrics pm ON um.USER_ID = pm.USER_ID
                LEFT JOIN SubmissionMetrics sm ON um.USER_ID = sm.USER_ID
                ORDER BY um.EnrolledCourses DESC");

            return results?.Select(r => new Dictionary<string, object>(r)).ToList() ?? new List<Dictionary<string, object>>();
        }

        private async Task<List<Dictionary<string, object>>> GenerateCoursePerformanceReport(SqlConnection connection)
        {
            // Optimize query with CTEs and better joins
            var results = await connection.QueryAsync(@"
                WITH EnrollmentMetrics AS (
                    SELECT 
                        ce.COURSE_ID,
                        COUNT(DISTINCT ce.USER_ID) as EnrolledStudents,
                        COUNT(DISTINCT CASE WHEN ce.COMPLETION_DATE IS NOT NULL THEN ce.USER_ID END) as CompletedStudents
                    FROM COURSE_ENROLLMENTS ce
                    GROUP BY ce.COURSE_ID
                ),
                ProgressMetrics AS (
                    SELECT 
                        cp.COURSE_ID,
                        AVG(CAST(cp.PROGRESS as FLOAT)) as AverageProgress
                    FROM COURSE_PROGRESS cp
                    GROUP BY cp.COURSE_ID
                ),
                SubmissionMetrics AS (
                    SELECT 
                        a.COURSE_ID,
                        COUNT(DISTINCT s.SUBMISSION_ID) as AssignmentSubmissions,
                        AVG(CAST(s.GRADE as FLOAT)) as AverageGrade
                    FROM ASSIGNMENTS a
                    LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID
                    GROUP BY a.COURSE_ID
                )
                SELECT 
                    c.COURSE_ID,
                    c.TITLE as CourseTitle,
                    u.FULL_NAME as Instructor,
                    ISNULL(em.EnrolledStudents, 0) as EnrolledStudents,
                    ISNULL(pm.AverageProgress, 0) as AverageProgress,
                    ISNULL(em.CompletedStudents, 0) as CompletedStudents,
                    ISNULL(sm.AssignmentSubmissions, 0) as AssignmentSubmissions,
                    ISNULL(sm.AverageGrade, 0) as AverageGrade
                FROM COURSES c
                JOIN USERS u ON c.CREATED_BY = u.USER_ID
                LEFT JOIN EnrollmentMetrics em ON c.COURSE_ID = em.COURSE_ID
                LEFT JOIN ProgressMetrics pm ON c.COURSE_ID = pm.COURSE_ID
                LEFT JOIN SubmissionMetrics sm ON c.COURSE_ID = sm.COURSE_ID
                ORDER BY em.EnrolledStudents DESC");

            return results?.Select(r => new Dictionary<string, object>(r)).ToList() ?? new List<Dictionary<string, object>>();
        }

        private async Task<List<Dictionary<string, object>>> GenerateLearningProgressReport(SqlConnection connection)
        {
            var data = new List<Dictionary<string, object>>();
            
            // Get learning progress metrics with corrected schema
            var results = await connection.QueryAsync(@"
                SELECT 
                    u.USER_ID,
                    u.FULL_NAME as StudentName,
                    c.TITLE as CourseTitle,
                    ce.ENROLLMENT_DATE,
                    cp.PROGRESS as CourseProgress,
                    COUNT(DISTINCT m.MODULE_ID) as TotalModules,
                    COUNT(DISTINCT ump.MODULE_ID) as CompletedModules,
                    COUNT(DISTINCT s.SUBMISSION_ID) as CompletedAssignments,
                    AVG(CAST(s.GRADE as FLOAT)) as AverageGrade,
                    MAX(s.SUBMITTED_ON) as LastActivityDate
                FROM USERS u
                JOIN COURSE_ENROLLMENTS ce ON u.USER_ID = ce.USER_ID
                JOIN COURSES c ON ce.COURSE_ID = c.COURSE_ID
                LEFT JOIN COURSE_PROGRESS cp ON ce.COURSE_ID = cp.COURSE_ID AND ce.USER_ID = cp.USER_ID
                LEFT JOIN MODULES m ON c.COURSE_ID = m.COURSE_ID
                LEFT JOIN USER_MODULE_PROGRESS ump ON m.MODULE_ID = ump.MODULE_ID AND u.USER_ID = ump.USER_ID
                LEFT JOIN ASSIGNMENTS a ON a.COURSE_ID = c.COURSE_ID
                LEFT JOIN ASSIGNMENT_SUBMISSIONS s ON a.ASSIGNMENT_ID = s.ASSIGNMENT_ID AND u.USER_ID = s.USER_ID
                GROUP BY u.USER_ID, u.FULL_NAME, c.TITLE, ce.ENROLLMENT_DATE, cp.PROGRESS
                ORDER BY u.FULL_NAME, c.TITLE");

            foreach (var row in results)
            {
                data.Add(new Dictionary<string, object>(row));
            }

            return data;
        }

        private async Task<List<Dictionary<string, object>>> GenerateCertificationStatsReport(SqlConnection connection)
        {
            var data = new List<Dictionary<string, object>>();
            
            // Get certification statistics with correct schema
            var results = await connection.QueryAsync(@"
                SELECT 
                    c.COURSE_ID,
                    c.TITLE as CourseTitle,
                    COUNT(DISTINCT ce.USER_ID) as EnrolledStudents,
                    COUNT(DISTINCT cert.CERTIFICATE_ID) as CertificatesIssued,
                    MIN(cert.ISSUE_DATE) as FirstCertificateDate,
                    MAX(cert.ISSUE_DATE) as LastCertificateDate,
                    AVG(CAST(cp.PROGRESS as FLOAT)) as AverageProgress,
                    COUNT(DISTINCT CASE WHEN cp.PROGRESS = 100 THEN ce.USER_ID END) as CompletedStudents
                FROM COURSES c
                LEFT JOIN COURSE_ENROLLMENTS ce ON c.COURSE_ID = ce.COURSE_ID
                LEFT JOIN CERTIFICATES cert ON c.COURSE_ID = cert.COURSE_ID
                LEFT JOIN COURSE_PROGRESS cp ON ce.COURSE_ID = cp.COURSE_ID AND ce.USER_ID = cp.USER_ID
                GROUP BY c.COURSE_ID, c.TITLE
                ORDER BY CertificatesIssued DESC");

            foreach (var row in results)
            {
                data.Add(new Dictionary<string, object>(row));
            }

            return data;
        }

        private byte[] GenerateExcelReport(List<Dictionary<string, object>> data, string reportType)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(reportType);

            // Add headers with styling
            var headers = data[0].Keys.ToList();
            for (int i = 0; i < headers.Count; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }

            // Add data with proper formatting
            for (int row = 0; row < data.Count; row++)
            {
                for (int col = 0; col < headers.Count; col++)
                {
                    var cell = worksheet.Cell(row + 2, col + 1);
                    var value = data[row][headers[col]];

                    if (value != null)
                    {
                        if (value is DateTime dateValue)
                        {
                            cell.Value = dateValue;
                            cell.Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                        }
                        else if (decimal.TryParse(value.ToString(), out decimal numValue))
                        {
                            cell.Value = numValue;
                            cell.Style.NumberFormat.Format = "#,##0.00";
                        }
                        else
                        {
                            cell.Value = value.ToString();
                        }
                    }
                }
            }

            // Auto-fit columns and add table styling
            worksheet.Columns().AdjustToContents();
            var table = worksheet.Range(1, 1, data.Count + 1, headers.Count).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        //private byte[] GeneratePdfReport(List<Dictionary<string, object>> data, string reportType)
        //{
        //    using var stream = new MemoryStream();
        //    var document = new Document(PageSize.A4.Rotate());
        //    var writer = PdfWriter.GetInstance(document, stream);

        //    document.Open();

        //    // Add title
        //    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 16);
        //    var title = new Paragraph(reportType.Replace("-", " ").ToUpper(), titleFont);
        //    title.Alignment = Element.ALIGN_CENTER;
        //    title.SpacingAfter = 20f;
        //    document.Add(title);

        //    // Create table
        //    var headers = data[0].Keys.ToList();
        //    var table = new PdfPTable(headers.Count);
        //    table.WidthPercentage = 100;

        //    // Add headers
        //    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
        //    foreach (var header in headers)
        //    {
        //        var cell = new PdfPCell(new Phrase(header, headerFont));
        //        cell.BackgroundColor = BaseColor.LIGHT_GRAY;
        //        cell.HorizontalAlignment = Element.ALIGN_CENTER;
        //        cell.Padding = 5;
        //        table.AddCell(cell);
        //    }

        //    // Add data
        //    var dataFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);
        //    foreach (var row in data)
        //    {
        //        foreach (var header in headers)
        //        {
        //            var value = row[header]?.ToString() ?? "";
        //            var cell = new PdfPCell(new Phrase(value, dataFont));
        //            cell.HorizontalAlignment = Element.ALIGN_LEFT;
        //            cell.Padding = 5;
        //            table.AddCell(cell);
        //        }
        //    }

        //    document.Add(table);
        //    document.Close();

        //    return stream.ToArray();
        //}

        private byte[] GenerateCsvReport(List<Dictionary<string, object>> data)
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);

            // Write headers
            writer.WriteLine(string.Join(",", data[0].Keys.Select(k => $"\"{k}\"")));

            // Write data with proper CSV escaping
            foreach (var row in data)
            {
                var values = row.Values.Select(v => $"\"{v?.ToString().Replace("\"", "\"\"")}\"");
                writer.WriteLine(string.Join(",", values));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
} 