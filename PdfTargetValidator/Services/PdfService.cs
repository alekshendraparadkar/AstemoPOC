using System.IO;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text.RegularExpressions;
using PdfTargetValidator.Interfaces;

namespace PdfTargetValidator.Services;

public class PdfService : IPdfService
{
    private readonly ILogger<PdfService> _logger;

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;
    }

    public string ExtractText(Stream pdfStream)
    {
        var textBuilder = new StringBuilder();

        using var pdfReader = new PdfReader(pdfStream);
        using var pdfDoc = new PdfDocument(pdfReader);

        int totalPages = pdfDoc.GetNumberOfPages();

        for (int page = 1; page <= totalPages; page++)
        {
            var strategy = new SimpleTextExtractionStrategy();

            string pageText = PdfTextExtractor.GetTextFromPage(
                pdfDoc.GetPage(page),
                strategy
            );

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                textBuilder.AppendLine(pageText);
            }
        }

        string extractedText = textBuilder.ToString();

        _logger.LogInformation("=== RAW EXTRACTED PDF TEXT ===");
        _logger.LogInformation("{ExtractedText}", extractedText);

        return extractedText;
    }
}