using System;
using System.IO;
using System.Collections.Generic;
using E_Learning_Platform.Models;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Kernel.Font;
using iText.IO.Font.Constants;
using iText.Layout.Properties;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using iText.Layout.Borders;
using Microsoft.AspNetCore.Mvc;
using ITextDocument = iText.Layout.Document;

namespace E_Learning_Platform.Services
{
    public class PdfReportService
    {
        public byte[] GenerateAnalyticsReport(ReportData reportData, List<ReportMetric> selectedMetrics)
        {
            using var memoryStream = new MemoryStream();
            var writer = new PdfWriter(memoryStream);
            var pdf = new PdfDocument(writer);
            var document = new ITextDocument(pdf, PageSize.A4.Rotate());

            try
            {
                // Add title
                var titleFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                var title = new Paragraph($"Analytics Report - {reportData.ReportType}")
                    .SetFont(titleFont)
                    .SetFontSize(20)
                    .SetTextAlignment(TextAlignment.CENTER);
                document.Add(title);

                // Add summary section
                var sectionFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                var summaryHeader = new Paragraph("Summary")
                    .SetFont(sectionFont)
                    .SetFontSize(16)
                    .SetTextAlignment(TextAlignment.LEFT);
                document.Add(summaryHeader);

                var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                foreach (var metric in reportData.Metrics)
                {
                    var metricParagraph = new Paragraph($"{metric.Key}: {metric.Value}")
                        .SetFont(normalFont)
                        .SetFontSize(12)
                        .SetTextAlignment(TextAlignment.LEFT);
                    document.Add(metricParagraph);
                }

                // Add detailed data
                var detailsHeader = new Paragraph("\nDetailed Data")
                    .SetFont(sectionFont)
                    .SetFontSize(16)
                    .SetTextAlignment(TextAlignment.LEFT);
                document.Add(detailsHeader);

                // Create table
                var columnCount = 2 + selectedMetrics.Count;
                var table = new Table(UnitValue.CreatePercentArray(columnCount))
                    .UseAllAvailableWidth()
                    .SetBorder(new SolidBorder(ColorConstants.BLACK, 1));

                // Add headers with styling
                var headerFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                
                // Create header cells
                var dateHeaderCell = new Cell()
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetFont(headerFont)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .Add(new Paragraph("Date"));

                var categoryHeaderCell = new Cell()
                    .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                    .SetFont(headerFont)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .Add(new Paragraph("Category"));

                table.AddHeaderCell(dateHeaderCell);
                table.AddHeaderCell(categoryHeaderCell);

                foreach (var metric in selectedMetrics)
                {
                    var metricHeaderCell = new Cell()
                        .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                        .SetFont(headerFont)
                        .SetTextAlignment(TextAlignment.CENTER)
                        .Add(new Paragraph(metric.Name));
                    table.AddHeaderCell(metricHeaderCell);
                }

                // Add data rows
                var dataFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                foreach (var row in reportData.Data)
                {
                    table.AddCell(new Cell()
                        .SetFont(dataFont)
                        .Add(new Paragraph(row.Date.ToString("d"))));

                    table.AddCell(new Cell()
                        .SetFont(dataFont)
                        .Add(new Paragraph(row.Category ?? "-")));

                    foreach (var metric in selectedMetrics)
                    {
                        var value = row.Values.ContainsKey(metric.Id) ? row.Values[metric.Id]?.ToString() ?? "-" : "-";
                        table.AddCell(new Cell()
                            .SetFont(dataFont)
                            .Add(new Paragraph(value)));
                    }
                }

                document.Add(table);
                return memoryStream.ToArray();
            }
            finally
            {
                document.Close();
            }
        }
    }
} 