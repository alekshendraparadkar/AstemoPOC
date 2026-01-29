using System.Text;
using System.Text.Json;
using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Models;

namespace PdfTargetValidator.Services;

public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LlmService> _logger;

    public LlmService(HttpClient httpClient, ILogger<LlmService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(byte[] pdfBytes, ValidationRequest validationRequest)
    {
        try
        {
            var prompt = BuildValidationPrompt(validationRequest);

            var response = await CallOpenAiAsync(prompt, pdfBytes);

            var result = ParseValidationResult(response);

            result = NormalizeValidationResult(result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PDF");
            return new ValidationResult
            {
                isValid = false,
                Message = $"Error: {ex.Message}",
                Mismatches = new List<ValidationMismatch>()
            };
        }
    }

    private string BuildValidationPrompt(ValidationRequest request)
    {
        var targets = string.Join(
        Environment.NewLine,
        request.Target2026.Select(t =>
            $"   - {t.Product}: {t.Target2026}")
    );

        return $@"
        You are a strict PDF validation assistant.
        You are given a scanned PDF document

        === EXPECTED VALUES ===
        1. AM Name: {request.AmName}
        2. Customer Name: {request.CustomerName}
        3. Target 2026 Values:
        {targets}


        === EXTRACTION RULES ===
        1. AM NAME EXTRACTION:
           - Find text after ""AM:"" in the PDF
           - Extract ONLY the person's name (typically 2-3 words)
           - Stop at punctuation, numbers, or common keywords like: Sales, Office, Contact, Region, Area, State, etc.
           - If you see ""ASHISH BHATTSSales"", extract only ""ASHISH BHATT"" (stop before duplicated letters or ALL CAPS field names)
           - Trim trailing whitespace and special characters
           - Compare case-insensitively

        2. CUSTOMER NAME EXTRACTION:
           - Find text after ""Customer:"" in the PDF
           - Look for pattern like ""[S]-29870 - A M AUTO SALES""
           - The name should only contain letters, numbers, spaces, and hyphens
           - Example: ""[S]-29870 - A M AUTO SALES"" → ""[S]-29870 - A M AUTO SALES""

        3. TARGET 2026 VALUES EXTRACTION:
           - Find the table with columns: Product Group, Target 2025, Achievement 2025, etc.
           - For each product (BRAKE PARTS, BRAKE FLUID, EMS, SUSPENSION, LUBES, OTHERS):
            * Locate the EXACT row where the Product Group name appears
            * Extract ONLY the value from the ""Target 2026"" column in THAT SAME ROW
            * DO NOT extract values from other rows like ""Over All"" or summary rows
   
           - CRITICAL: When extracting ""OTHERS"" target value:
            * Look for the row that starts with ""OTHERS"" (the word, not the letter O)
            * The ""OTHERS"" row is DIFFERENT from the ""Over All"" row
            * DO NOT confuse the letter ""O"" in ""Over All"" with the digit ""0""
            * Example table structure:
              LUBES          ...    ...    ...
              OTHERS         -      2,22,020  -     -    -    10,00,000  ← Extract from THIS row
              Over All       ...    ...    ...                85,00,000  ← This is a DIFFERENT row
   
           - Remove all commas and spaces before comparing
           - Indian number format examples:
             * ""70,00,000"" = 7000000
             * ""10,00,000"" = 1000000
             * ""5,00,000"" = 500000
           - Extract only numeric values (digits 0-9)
           - Ignore any alphabetic characters when extracting numbers 

        4. SIGNATURE VALIDATION:
            - Signature is MANDATORY for this document.
            - If Signature Detected = false → add mismatch:
                field = ""Signature""
                expectedValue = ""Present""
                pdfValue = ""Not Present""
                reason = ""Signature is mandatory but not found in document""
     
        === VALIDATION RULES ===
        1. AM Name: Must match {request.AmName} (case-insensitive)
        2. Customer Name: Must match {request.CustomerName} (case-insensitive)
        3. Target Values: Must match exactly the numeric values
        4. Signature: Must match expected presence ({request.IsSignatureDetected})



        === OUTPUT FORMAT ===
        Return ONLY valid JSON in this format:
        Do NOT add trailing commas.
        Do NOT add markdown.

        {{
        ""isValid"": true/false,
        ""message"": ""Brief summary"",
        ""mismatches"": [
             {{
            ""field"": ""AM Name | Customer Name | <Product> Target | Signature"",
              ""expectedValue"": ""string"",
              ""pdfValue"": ""string"",
              ""reason"": ""short explanation""
    }}
  ]
}}

        === IMPORTANT INSTRUCTIONS ===
        
        1. For numeric values, handle both formats:
           -Indian format: ""70,00,000""(remove commas)
           - Standard format: ""7000000""


        2.Return ONLY valid JSON in the specified format;

";
    }

    private async Task<string> CallOpenAiAsync(string prompt, byte[] pdfBytes)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("OpenAI key not set");

        _logger.LogInformation("Sending validation prompt to OpenAI");
        
        var pdfBase64 = "data:application/pdf;base64," + Convert.ToBase64String(pdfBytes);

        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new
                        {
                            type = "file",
                            file = new
                            {
                                filename = "document.pdf",
                                file_data = pdfBase64
                            }
                        }
                    }
                }
            },
            temperature = 0,
            max_tokens = 1200
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = content
        };

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI Error: {Status}", response.StatusCode);
            _logger.LogError("Error Body: {Body}", responseContent);
            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("Raw OpenAI Response: {RawResponse}", responseContent);

        using var doc = JsonDocument.Parse(responseContent);

        var message = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var contentElement = message.GetProperty("content");

        string result;

        if (contentElement.ValueKind == JsonValueKind.String)
        {
            result = contentElement.GetString() ?? string.Empty;
        }
        else if (contentElement.ValueKind == JsonValueKind.Array)
        {
            result = contentElement[0].GetProperty("text").GetString() ?? string.Empty;
        }
        else
        {
            result = string.Empty;
        }

        _logger.LogInformation("Raw LLM Output (before parsing): {LlmOutput}", result);

        return result;
    }

    private ValidationResult ParseValidationResult(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return CreateDefaultValidationResult("Empty response from AI service");

        var cleanedResponse = CleanJsonResponse(response);
        _logger.LogInformation("Cleaned JSON Response: {CleanedResponse}", cleanedResponse);

        try
        {
            var result = JsonSerializer.Deserialize<ValidationResult>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null)
                return CreateDefaultValidationResult("Invalid JSON structure from AI");

            _logger.LogInformation("Parsed ValidationResult: Valid={IsValid}, Mismatches={MismatchCount}", result.isValid, result.Mismatches?.Count ?? 0);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI JSON. Raw response: {Response}", response);
            return CreateDefaultValidationResult("AI response was not valid JSON");
        }
    }

    private string CleanJsonResponse(string response)
    {
        response = System.Text.RegularExpressions.Regex.Replace(
            response,
            @"```json\s*|\s*```",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        response = response.Trim();

        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');

        if (start >= 0 && end > start)
            response = response.Substring(start, end - start + 1);

        return response;
    }

    private ValidationResult CreateDefaultValidationResult(string message)
    {
        return new ValidationResult
        {
            isValid = false,
            Message = message,
            Mismatches = new List<ValidationMismatch>()
        };
    }

    private ValidationResult NormalizeValidationResult(ValidationResult result)
    {
        if (result == null)
            return CreateDefaultValidationResult("Validation result is null");

        if (result.Mismatches == null)
            result.Mismatches = new List<ValidationMismatch>();

        // Filter out false positives: keep only actual mismatches where values don't match
        var actualMismatches = new List<ValidationMismatch>();
        foreach (var mismatch in result.Mismatches)
        {
            if (!string.IsNullOrEmpty(mismatch.PdfValue))
                mismatch.PdfValue = mismatch.PdfValue.Trim();

            if (!string.IsNullOrEmpty(mismatch.ExpectedValue))
                mismatch.ExpectedValue = mismatch.ExpectedValue.Trim();

            // Only add to actual mismatches if the values don't match (case-insensitive comparison)
            if (!string.Equals(mismatch.PdfValue, mismatch.ExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                actualMismatches.Add(mismatch);
                _logger.LogWarning("Mismatch found - Field: {Field}, Expected: {Expected}, Got: {Got}", 
                    mismatch.Field, mismatch.ExpectedValue, mismatch.PdfValue);
            }
            else
            {
                _logger.LogInformation("Field matches - {Field}: {Value}", mismatch.Field, mismatch.PdfValue);
            }
        }

        result.Mismatches = actualMismatches;
        result.isValid = !result.Mismatches.Any();

        if (result.isValid)
            result.Message = "All fields match successfully";
        else if (string.IsNullOrWhiteSpace(result.Message))
            result.Message = "One or more validations failed";

        _logger.LogInformation("Normalized Result: IsValid={IsValid}, Message={Message}, MismatchCount={MismatchCount}", 
            result.isValid, result.Message, result.Mismatches.Count);

        return result;
    }
}

