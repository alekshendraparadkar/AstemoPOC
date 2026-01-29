using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using System.Text;
using System.Text.Json;
using PdfTargetValidator.Interfaces;

namespace PdfTargetValidator.Services;

public class SignatureService : ISignatureService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SignatureService> _logger;

    public SignatureService(HttpClient httpClient, ILogger<SignatureService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsSignaturePresentAsync(IFormFile pdfFile)
    {
        using var ms = new MemoryStream();
        await pdfFile.CopyToAsync(ms);
        var pdfBytes = ms.ToArray();

        var base64Image = ConvertPdfFirstPageToBase64(pdfBytes);

        return await AskLlmIfSignaturePresent(base64Image);
    }

    private string ConvertPdfFirstPageToBase64(byte[] pdfBytes)
    {
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(1080, 1920));
        using var pageReader = docReader.GetPageReader(0);

        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();

        var rawBytes = pageReader.GetImage(); 
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(rawBytes, width, height);

        using var outStream = new MemoryStream();
        image.Save(outStream, new PngEncoder());

        return Convert.ToBase64String(outStream.ToArray());
    }

    private async Task<bool> AskLlmIfSignaturePresent(string base64Image)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("OpenAI key not set");

        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new object[]
            {
                new {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Is there a handwritten or digital signature in this document image? Reply only true or false." },
                        new {
                            type = "image_url",
                            image_url = new {
                                url = $"data:image/png;base64,{base64Image}"
                            }
                        }
                    }
                }
            },
            temperature = 0
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(responseBody);

        var answer = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?
            .Trim()
            .ToLower();

        _logger.LogInformation("Signature detection response: {Answer}", answer);

        return answer == "true";
    }
}
