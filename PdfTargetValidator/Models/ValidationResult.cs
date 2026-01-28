namespace PdfTargetValidator.Models;

public class ValidationResult
{
    public bool isValid { get; set; }
    public List<ValidationMismatch> Mismatches { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ValidationMismatch
{
    public string Field { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string PdfValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}