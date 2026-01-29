namespace PdfTargetValidator.Interfaces
{
public interface ISignatureService
{
    Task<bool> IsSignaturePresentAsync(IFormFile pdfFile);
}
}