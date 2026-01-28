using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Utils;

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
        using var pdfDocument = PdfDocument.Open(pdfStream);

        var textBuilder = new StringBuilder();
        
        foreach (var page in pdfDocument.GetPages())
        {
            var pageText = page.Text;
            
            _logger.LogInformation("=== RAW PDF TEXT (Page) ===");
            _logger.LogInformation("{RawText}", pageText);
            
            if (!string.IsNullOrEmpty(pageText))
            {
                // Clean up the extracted text with basic cleaning
                var cleanedText = CleanPdfText(pageText);
                
                _logger.LogInformation("=== CLEANED PDF TEXT (Page) ===");
                _logger.LogInformation("{CleanedText}", cleanedText);
                
                textBuilder.AppendLine(cleanedText);
            }
        }

        var finalText = textBuilder.ToString();
        
        // Apply advanced preprocessing to structure the text better
        var preprocessedText = PdfTextPreprocessor.PreprocessPdfText(finalText);
        
        _logger.LogInformation("=== FINAL EXTRACTED TEXT ===");
        _logger.LogInformation("{FinalText}", finalText);
        
        _logger.LogInformation("=== PREPROCESSED TEXT (Ready for LLM) ===");
        _logger.LogInformation("{PreprocessedText}", preprocessedText);
        
        // Log extracted fields for debugging
        var fields = PdfTextPreprocessor.ExtractKeyFields(preprocessedText);
        _logger.LogInformation("=== EXTRACTED FIELDS ===");
        _logger.LogInformation("AM Name: {AmName}", fields.AmName);
        _logger.LogInformation("Region: {Region}", fields.Region);
        _logger.LogInformation("Customer Name: {CustomerName}", fields.CustomerName);
        _logger.LogInformation("Sales Office: {SalesOffice}", fields.SalesOffice);
        _logger.LogInformation("BRAKE PARTS Target: {BrakePartsTarget}", fields.BrakePartsTarget);
        _logger.LogInformation("BRAKE FLUID Target: {BrakeFluidTarget}", fields.BrakeFluidTarget);
        _logger.LogInformation("OTHERS Target: {OthersTarget}", fields.OthersTarget);

        return preprocessedText;
    }

    private string CleanPdfText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // First pass: Remove spaces between single characters (PDF character spacing issue)
        // Pattern: single char space single char space single char
        // This handles cases like "A S H I S H" → "ASHISH"
        text = Regex.Replace(text, @"(?<![A-Za-z0-9])\s(?=[A-Za-z0-9]\s)", "");
        text = Regex.Replace(text, @"([A-Za-z0-9])\s(?=[A-Za-z0-9]\s[A-Za-z0-9])", "$1");
        
        // Better approach: if we have pattern like "X Y Z" where each is single char, remove spaces
        var sb = new StringBuilder();
        var chars = text.ToCharArray();
        
        for (int i = 0; i < chars.Length; i++)
        {
            char current = chars[i];
            
            // Check if this is a space between two single letters (likely PDF spacing issue)
            bool isPrevCharLetter = i > 0 && char.IsLetter(chars[i - 1]);
            bool isNextCharLetter = i < chars.Length - 1 && char.IsLetter(chars[i + 1]);
            bool isCurrentSpace = char.IsWhiteSpace(current);
            
            // Check if next-next is space and letter (pattern: CHAR SPACE CHAR SPACE CHAR)
            bool isSpacedLetterPattern = isCurrentSpace && isPrevCharLetter && isNextCharLetter &&
                                         (i < chars.Length - 2 && char.IsWhiteSpace(chars[i + 2]));
            
            // Skip spaces in the spaced letter pattern
            if (isSpacedLetterPattern)
            {
                continue;
            }
            
            // For other whitespace, normalize it
            if (char.IsWhiteSpace(current))
            {
                // Skip multiple consecutive whitespaces
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(current);
            }
        }

        // Final cleanup: normalize multiple spaces to single space
        string result = Regex.Replace(sb.ToString(), @"\s+", " ");
        
        // Preserve line breaks
        result = Regex.Replace(result, @" *\n+ *", "\n");
        
        return result.Trim();
    }
}
