using System.Text;
using System.Text.RegularExpressions;

namespace PdfTargetValidator.Utils;

public static class PdfTextPreprocessor
{
    public static string PreprocessPdfText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return rawText;

        var sb = new StringBuilder();

        // Step 1: Add line breaks after known field names to separate them
        // This handles cases like "AM:ASHISH BHATTSales Office:North"
        var text = rawText;
        
        // Add line breaks before common field keywords
        text = Regex.Replace(text, @"(?<![\r\n])(Sales Office:|Region:|Customer:|Product|Group|Target|Achievement|Growth|Policy|Signature|Date|CIN|A606|International)", "\n$1");
        
        // Step 2: Add space before "Sales" if it comes directly after a name (no space)
        // e.g., "BHATTSales" → "BHATT\nSales"
        text = Regex.Replace(text, @"([A-Z])([A-Z]+)Sales(?=\s|Office)", "$1$2\nSales");
        
        // Step 3: Clean up customer field
        // "[S]- 29870 -AM AUTO SALES" → "AM AUTO SALES"
        text = Regex.Replace(text, @"\[S\]\s*-\s*\d+\s*-\s*", "");
        
        // Step 4: Add space between numbers and letters when they're concatenated
        // "Target2026BRAKEPARTS" → "Target 2026 BRAKE PARTS"
        text = Regex.Replace(text, @"(\d)([A-Z])", "$1 $2");
        text = Regex.Replace(text, @"([a-z])(\d)", "$1 $2");
        
        // Step 5: Clean up number formatting - add spaces in Indian format for clarity
        // Keep the numbers but make them more readable: "48,10,37656,28,556" 
        // This is already in the right format, just ensure consistency
        
        // Step 6: Remove percentage signs and other symbols that might cause issues
        text = text.Replace("%", " percent");
        
        // Step 7: Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\n\s+", "\n");
        text = Regex.Replace(text, @"\s+\n", "\n");
        
        // Step 8: Preserve line breaks and structure
        var lines = text.Split(new[] { "\n" }, StringSplitOptions.None);
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (!string.IsNullOrEmpty(trimmedLine))
            {
                sb.AppendLine(trimmedLine);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extract specific fields from preprocessed PDF text
    /// Useful for validating that the text contains the expected data structure
    /// </summary>
    public static PdfFieldsExtract ExtractKeyFields(string preprocessedText)
    {
        var result = new PdfFieldsExtract();

        if (string.IsNullOrEmpty(preprocessedText))
            return result;

        // Extract AM Name
        var amMatch = Regex.Match(preprocessedText, @"AM:?\s*([A-Z\s]+?)(?=\n|Sales Office|$)", RegexOptions.IgnoreCase);
        if (amMatch.Success)
        {
            result.AmName = amMatch.Groups[1].Value.Trim();
        }

        // Extract Region
        var regionMatch = Regex.Match(preprocessedText, @"Region:?\s*([A-Za-z\s]+?)(?=\n|AM:|$)", RegexOptions.IgnoreCase);
        if (regionMatch.Success)
        {
            result.Region = regionMatch.Groups[1].Value.Trim();
        }

        // Extract Customer Name
        var customerMatch = Regex.Match(preprocessedText, @"Customer:?\s*([A-Z\s\-]+?)(?=\n|Product|$)", RegexOptions.IgnoreCase);
        if (customerMatch.Success)
        {
            result.CustomerName = customerMatch.Groups[1].Value.Trim();
        }

        // Extract sales office
        var officeMatch = Regex.Match(preprocessedText, @"Sales Office:?\s*([A-Za-z\s]+?)(?=\n|Customer|$)", RegexOptions.IgnoreCase);
        if (officeMatch.Success)
        {
            result.SalesOffice = officeMatch.Groups[1].Value.Trim();
        }

        // Extract Target values from the table
        var lines = preprocessedText.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            // Look for product lines with target values
            if (line.Contains("BRAKE PARTS", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTargetValue(line, "BRAKE PARTS", result);
            }
            else if (line.Contains("BRAKE FLUID", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTargetValue(line, "BRAKE FLUID", result);
            }
            else if (line.Contains("OTHERS", StringComparison.OrdinalIgnoreCase) && !line.Contains("Over All"))
            {
                ExtractTargetValue(line, "OTHERS", result);
            }
        }

        return result;
    }

    private static void ExtractTargetValue(string line, string productName, PdfFieldsExtract result)
    {
        // Extract the last number in the line (which should be the 2026 target)
        var numbers = Regex.Matches(line, @"\d+(?:,\d+)*(?:,\d+)?");
        
        if (numbers.Count > 0)
        {
            var lastNumber = numbers[numbers.Count - 1].Value;
            // Remove commas
            var cleanNumber = lastNumber.Replace(",", "");
            
            if (productName.Equals("BRAKE PARTS", StringComparison.OrdinalIgnoreCase))
                result.BrakePartsTarget = cleanNumber;
            else if (productName.Equals("BRAKE FLUID", StringComparison.OrdinalIgnoreCase))
                result.BrakeFluidTarget = cleanNumber;
            else if (productName.Equals("OTHERS", StringComparison.OrdinalIgnoreCase))
                result.OthersTarget = cleanNumber;
        }
    }
}

public class PdfFieldsExtract
{
    public string AmName { get; set; }
    public string Region { get; set; }
    public string CustomerName { get; set; }
    public string SalesOffice { get; set; }
    public string BrakePartsTarget { get; set; }
    public string BrakeFluidTarget { get; set; }
    public string OthersTarget { get; set; }
}
