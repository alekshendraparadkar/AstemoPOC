namespace PdfTargetValidator.Models;

public class ValidationRequest
{
    public string AmName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public List<ProductTarget2026> Target2026 { get; set; } = new();
    public bool IsSignatureDetected { get; set; }

}

public class ProductTarget2026
{
    public string Product { get; set; } = string.Empty;
    public decimal Target2026 { get; set; }
}