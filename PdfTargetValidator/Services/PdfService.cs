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

        // Identify spaced-out letter patterns that need to be joined
        // Pattern: Single letter followed by space, repeated at least 4+ times (like "A S H I S H" → "ASHISH")
        // But preserve intentional abbreviations with spaces like "A M AUTO"
        text = RemoveExcessiveLetterSpacing(text);
        
        // Normalize whitespace
        var sb = new StringBuilder();
        var chars = text.ToCharArray();
        
        for (int i = 0; i < chars.Length; i++)
        {
            char current = chars[i];
            
            // For whitespace, normalize it
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

    private string RemoveExcessiveLetterSpacing(string text)
    {
        // Only remove spaces from patterns like "A S H I S H B H A T T" (single letters spaced)
        // DO NOT remove spaces from "A M AUTO SALES" (intentional abbreviations)
        // Pattern: Detect continuous sequences of single letters separated by spaces
        // If we find 4+ single letters in a row separated by spaces, join them
        
        var sb = new StringBuilder();
        var chars = text.ToCharArray();
        int i = 0;
        
        while (i < chars.Length)
        {
            // Look for pattern: LETTER SPACE LETTER SPACE LETTER SPACE LETTER
            // Count consecutive single letters separated by spaces
            int letterCount = 0;
            int startIdx = i;
            int j = i;
            
            while (j < chars.Length)
            {
                if (char.IsLetter(chars[j]))
                {
                    letterCount++;
                    j++;
                    
                    // Check if followed by space and another letter
                    if (j < chars.Length && char.IsWhiteSpace(chars[j]))
                    {
                        j++; // Skip space
                        if (j < chars.Length && char.IsLetter(chars[j]))
                        {
                            // Continue pattern
                            continue;
                        }
                        else
                        {
                            // Pattern breaks, back up
                            j--;
                            break;
                        }
                    }
                    else if (j < chars.Length && char.IsLetter(chars[j]))
                    {
                        // No space between letters, pattern ends
                        break;
                    }
                    else
                    {
                        // End of string or non-letter, end pattern
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            
            // If we found 4+ single letters separated by spaces, remove the spaces between them
            if (letterCount >= 4)
            {
                int k = startIdx;
                while (k < j)
                {
                    if (char.IsLetter(chars[k]))
                    {
                        sb.Append(chars[k]);
                    }
                    // Skip spaces in this pattern
                    k++;
                }
                i = j;
            }
            else
            {
                // Not a spaced-out word, keep as-is
                sb.Append(chars[i]);
                i++;
            }
        }
        
        return sb.ToString();
    }
}
