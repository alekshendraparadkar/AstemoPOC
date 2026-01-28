using Microsoft.AspNetCore.Mvc;
using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Models;

namespace PdfTargetValidator.Controllers;

[ApiController]
[Route("api/validation")]
public class ValidationController : ControllerBase
{
    private readonly IPdfService _pdfService;
    private readonly ILlmService _llmService;

    public ValidationController(IPdfService pdfService, ILlmService llmService)
    {
        _pdfService = pdfService;
        _llmService = llmService;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidatePdf(IFormFile pdf)
    {
        if (pdf == null || pdf.Length == 0)
            return BadRequest(new { error = "Please Upload a pdf file" });

        var testValidationrequest = new ValidationRequest
        {
            AmName = "ASHISH BHATT",
            CustomerName = "A M AUTO SALES",

            Target2026 = new List<ProductTarget2026>
            {
                new ProductTarget2026 {
                    Product = "BRAKE PARTS",
                    Target2026 = 7000000
                },
                new ProductTarget2026 {
                    Product = "BRAKE FLUID",
                    Target2026 = 500000
                },
                new ProductTarget2026 {
                    Product = "OTHERS",
                    Target2026 = 1000000
                }
            }
        };

        try
        {
            using var stream = pdf.OpenReadStream();
            var pdfText = _pdfService.ExtractText(stream);

            var result = await _llmService.ValidateAsync(pdfText, testValidationrequest);

            return Ok(new
            {
                success = true,
                isValid = result.isValid,
                message = result.Message,
                mismatches = result.Mismatches,
                testDataUsed = new
                {
                    amName = testValidationrequest.AmName,
                    customerName = testValidationrequest.CustomerName,
                    targets = testValidationrequest.Target2026.Select(t => new
                    {
                        product = t.Product,
                        target = t.Target2026
                    })
                }
            });

        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message
            });
        }
    }



}