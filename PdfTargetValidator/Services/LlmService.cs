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

    public async Task<ValidationResult> ValidateAsync(string pdfText, ValidationRequest validationRequest)
    {
        try
        {
            _logger.LogInformation("=== PREPROCESSED PDF TEXT SENT TO LLM ===");
            _logger.LogInformation("{PdfText}", pdfText);

            var prompt = BuildValidationPrompt(pdfText, validationRequest);
            var response = await CallOpenAiAsync(prompt);

            var result = ParseValidationResult(response);

            result = NormalizeValidationResult(result);
            if (!validationRequest.IsSignatureDetected)
            {
                result.isValid = false;

                if (!result.Mismatches.Any(m => m.Field.Equals("Signature", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Mismatches.Add(new ValidationMismatch
                    {
                        Field = "Signature",
                        ExpectedValue = "Present",
                        PdfValue = "Not Present",
                        Reason = "Signature is mandatory but not found in document"
                    });
                }

                result.Message = "Validation failed: mandatory signature missing";
            }


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

    private string BuildValidationPrompt(string pdfText, ValidationRequest request)
    {
        var targets = string.Join(
            Environment.NewLine,
            request.Target2026.Select(t =>
                $"   - {t.Product}: {t.Target2026}")
        );

        return $@"
        You are a PDF validation assistant. Validate the following fields from the PDF content.

        === EXPECTED VALUES ===
        1. AM Name: {request.AmName}
        2. Customer Name: {request.CustomerName}
        3. Target 2026 Values:
        {targets}
        4. Signature Present: {request.IsSignatureDetected}


        === PDF CONTENT ===
        {pdfText}

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
          ""message"": ""Brief summary message"",
          ""mismatches"": [
            {{
              ""field"": ""AM Name"",
              ""expectedValue"": ""{request.AmName}"",
              ""pdfValue"": ""Value extracted from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{
              ""field"": ""Customer Name"",
              ""expectedValue"": ""{request.CustomerName}"",
              ""pdfValue"": ""Value extracted from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{
              ""field"": ""BRAKE PARTS Target"",
              ""expectedValue"": ""56800"",
              ""pdfValue"": ""Value from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{
              ""field"": ""BRAKE FLUID Target"",
              ""expectedValue"": ""456500"",
              ""pdfValue"": ""Value from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{""field"": ""Signature"",
              ""expectedValue"": ""true"",
              ""pdfValue"": ""true or false"",
              ""reason"": ""Signature missing if expected true but not found""
            }},
          ]
        }}

        === IMPORTANT INSTRUCTIONS ===
        1.AM Name extraction is critical:
           -If you see ""ASHISH BHATTS"" but expected is ""ASHISH BHATT"", the 'S' is likely from the next field
           - Look for transitions from lowercase to UPPERCASE to identify field boundaries
           - Extract names by stopping at common field keywords: Sales, Office, Contact, Region, Area, Code, etc.
           - Common pattern: ""KEYWORD + NAME"" → Extract only ""NAME""
                
        2. For numeric values, handle both formats:
           -Indian format: ""70,00,000""(remove commas)
           - Standard format: ""7000000""


        3.Return ONLY valid JSON in the specified format
        
        4. If extracted value has trailing characters from adjacent fields, exclude them:
           -""BHATTS"" → ""BHATT"" (remove extra S from adjacent field)
           - ""SALESA"" → ""SALES"" (remove adjacent characters)
        ";

    }

    private async Task<string> CallOpenAiAsync(string prompt)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Open AI key not set");

        var requestBody = new
        {
            model = "gpt-4o-mini",
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.1,
            max_tokens = 1200,
            response_format = new { type = "json_object" }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        _logger.LogDebug("Request body length: {Length} chars", jsonBody.Length);

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = content
        };


        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);


        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "PdfTargetValidator/1.0");

        try
        {
            var response = await _httpClient.SendAsync(request);

            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API Error - Status Code: {StatusCode}", response.StatusCode);
                _logger.LogError("Response Headers: {Headers}", response.Headers.ToString());
                _logger.LogError("Response Body: {Body}", responseContent);

                response.EnsureSuccessStatusCode();
            }

            _logger.LogDebug("OpenAI Response successful");

            using var doc = JsonDocument.Parse(responseContent);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP request failed");
            throw;
        }
    }


    private ValidationResult ParseValidationResult(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return CreateDefaultValidationResult("Empty response from AI service");

        var cleanedResponse = CleanJsonResponse(response);

        try
        {
            var result = JsonSerializer.Deserialize<ValidationResult>(
                cleanedResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null)
                return CreateDefaultValidationResult("Invalid JSON structure from AI");

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI JSON. Raw response: {Response}", response);

            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                response,
                @"```json\s*(.*?)\s*```|```\s*(.*?)\s*```",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (jsonMatch.Success)
            {
                var jsonContent = jsonMatch.Groups[1].Success
                    ? jsonMatch.Groups[1].Value
                    : jsonMatch.Groups[2].Value;

                try
                {
                    return JsonSerializer.Deserialize<ValidationResult>(
                               jsonContent,
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                           ?? CreateDefaultValidationResult("Parsed markdown JSON but got null");
                }
                catch
                {
                    return CreateDefaultValidationResult("Failed to parse JSON from markdown block");
                }
            }

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

    /// <summary>
    /// Normalize validation results after LLM processing
    /// With pre-processed text, we only need minimal cleanup for case-insensitive comparisons
    /// </summary>
    private ValidationResult NormalizeValidationResult(ValidationResult result)
    {
        if (result?.Mismatches == null)
            return result;


        foreach (var mismatch in result.Mismatches)
        {
            if (mismatch.Field.Equals("Signature", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(mismatch.PdfValue))
                mismatch.PdfValue = mismatch.PdfValue.Trim();

            if (!string.IsNullOrEmpty(mismatch.ExpectedValue))
                mismatch.ExpectedValue = mismatch.ExpectedValue.Trim();

            if (!string.IsNullOrEmpty(mismatch.PdfValue) &&
                !string.IsNullOrEmpty(mismatch.ExpectedValue) &&
                mismatch.PdfValue.Equals(mismatch.ExpectedValue, StringComparison.OrdinalIgnoreCase))
            {
                mismatch.Reason = "Value matches (case-insensitive)";
            }
        }
        // Update validation status
        result.isValid = !result.Mismatches.Any();


        if (result.isValid)
            result.Message = "All fields match successfully";
        else if (string.IsNullOrWhiteSpace(result.Message))
            result.Message = "One or more validations failed";

        return result;
    }

}
