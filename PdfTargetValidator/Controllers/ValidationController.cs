using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Models;

namespace PdfTargetValidator.Controllers;

[ApiController]
[Route("api/validation")]
public class ValidationController : ControllerBase
{
    private readonly ILlmService _llmService;

    public ValidationController(ILlmService llmService)
    {
        _llmService = llmService;

    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadPdf([FromForm] PdfUploadForm form)
    {
        if (form.File == null || form.File.Length == 0)
            return BadRequest("No file uploaded");

        // ✅ Hardcoded test values (OK for now)
        var validationRequest = new ValidationRequest
        {
            AmName = "ATUL CHAKRAWAR",
            CustomerName = "[S]- 28661 - VOHRA DISTRIBUTORS",
            Target2026 = new List<ProductTarget2026>
            {
                new() { Product = "BRAKE PARTS", Target2026 = 7080858 },
                new() { Product = "BRAKE FLUID", Target2026 = 4775806 },
            },
        };

        using var ms = new MemoryStream();
        await form.File.CopyToAsync(ms);

        var result = await _llmService.ValidateAsync(ms.ToArray(), validationRequest);

        return Ok(result);
    }
}