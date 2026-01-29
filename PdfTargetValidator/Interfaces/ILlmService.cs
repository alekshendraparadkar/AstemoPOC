using PdfTargetValidator.Models;

namespace PdfTargetValidator.Interfaces;

public interface ILlmService
{
    Task<ValidationResult> ValidateAsync(byte[] pdfBytes, ValidationRequest validationRequest);
}