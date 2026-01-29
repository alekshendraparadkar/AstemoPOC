using System.IO;
using System.Text;
using UglyToad.PdfPig;
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
        using var pdfDocument = PdfDocument.Open(pdfStream);
        var textBuilder = new StringBuilder();

        foreach (var page in pdfDocument.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                var cleanedText = PreprocessPdfText(pageText);
                textBuilder.AppendLine(cleanedText);
            }

        }

        var extractedText = textBuilder.ToString();
        _logger.LogInformation("=== EXTRACTED PDF TEXT ===");
        _logger.LogInformation("{ExtractedText}", extractedText);

        return extractedText;
    }
    private string PreprocessPdfText(string text)
    {
        text = NormalizeLineBreaks(text);
        text = NormalizeWhitespace(text);

        text = SeparateConcatenatedFields(text);
        text = FixNameFieldIssues(text);
        text = CleanCustomerField(text);

        text = SeparateNumbersAndLetters(text);
        text = FixDuplicateCharacters(text);

        text = RemoveConfusingSpecialChars(text);
        text = StructureWithLineBreaks(text);
        text = ExtractProductTarget2026Only(text);


        return text.Trim();
    }
    private string NormalizeLineBreaks(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private string NormalizeWhitespace(string text)
    {
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{2,}", "\n\n");
        return text.Trim();
    }
    private string SeparateConcatenatedFields(string text)
    {
        var keywords = new[]
        {
            "Sales", "Office", "Region", "Area", "State", "Customer", "Target", "Product"
        };

        foreach (var k in keywords)
        {
            text = Regex.Replace(text, $@"([a-zA-Z])({k})", "$1\n$2");
        }

        // Normalize label formatting
        text = Regex.Replace(text, @"(AM|Customer)\s*:", "$1: ");

        return text;
    }

    private string FixNameFieldIssues(string text)
    {
        text = Regex.Replace(
            text,
            @"\b([A-Z]{3,})(S)(\s*(Sales|Office|Region|Area))",
            "$1\n$3");

        return text;
    }
    private string CleanCustomerField(string text)
    {
        text = Regex.Replace(
            text,
            @"Customer\s*:\s*",
            "Customer: ",
            RegexOptions.IgnoreCase);
        return text;
    }
    private string SeparateNumbersAndLetters(string text)
    {
        text = Regex.Replace(text, @"([A-Za-z])(\d)", "$1 $2");
        text = Regex.Replace(text, @"(\d)([A-Za-z])", "$1 $2");
        return text;
    }


    private string FixDuplicateCharacters(string text)
    {

        return Regex.Replace(text, @"([A-Za-z])\1{1,}", "$1$1");
    }


    private string RemoveConfusingSpecialChars(string text)
    {
        // Allow square brackets for customer code like [S]
        return Regex.Replace(text, @"[^\w\s:.,\-\[\]\/\n]", "");
    }


private string StructureWithLineBreaks(string text)
{
    var products = new[]
    {
        "BRAKEPARTS", "BRAKE PARTS",
        "BRAKEFLUID", "BRAKE FLUID",
        "EMS", "LUBES", "SUSPENSION",
        "OTHERS",
        "OVERALL", "OVER ALL"
    };

    // Insert newline BEFORE product even if stuck to number
    foreach (var p in products)
    {
        text = Regex.Replace(
            text,
            $@"(?i){p}",
            "\n" + p);
    }

    text = Regex.Replace(
        text,
        @"Target\s*2026",
        "\nTarget 2026\n",
        RegexOptions.IgnoreCase);

    text = Regex.Replace(
        text,
        @"Customer Signature",
        "\n\nCustomer Signature",
        RegexOptions.IgnoreCase);

    return text;
}

private string ExtractProductTarget2026Only(string text)
{
    var sb = new StringBuilder();

    sb.AppendLine();
    sb.AppendLine("=== EXTRACTED TARGET 2026 VALUES ===");

    var lines = text
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim())
        .ToList();

    foreach (var line in lines)
    {
        if (!Regex.IsMatch(line, @"^(BRAKE|EMS|LUBES|SUSPENSION|OTHERS)", RegexOptions.IgnoreCase))
            continue;

        var nums = Regex.Matches(line, @"\d[\d,]*");

        if (nums.Count == 0)
            continue;

        // LAST number = Target 2026
        var last = nums[^1].Value.Replace(",", "");

        var product = Regex.Match(line, @"^[A-Za-z\s]+").Value.Trim();

        sb.AppendLine($"PRODUCT: {product} | TARGET2026: {last}");
    }

    return text + sb.ToString();   
}
}