using PdfTargetValidator.Models;

namespace PdfTargetValidator.Interfaces;

public interface ILlmService
{
    Task<ValidationResult> ValidateAsync(string pdfText, ValidationRequest validationRequest);
}