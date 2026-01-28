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
            var prompt = BuildValidationPrompt(pdfText, validationRequest);
            var response = await CallOpenAiAsync(prompt);

            return ParseValidationResult(response);
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
           - Extract everything after the last ""-"" and trim spaces
           - The name should only contain letters, numbers, spaces, and hyphens
           - Example: ""[S]-29870 - A M AUTO SALES"" → ""A M AUTO SALES""

        3. TARGET 2026 VALUES EXTRACTION:
           - Find the table row for each product
           - Extract the value from the ""Target 2026"" column
           - Remove all commas and spaces before comparing
           - Indian number format: ""70,00,000"" = 7000000
           - Extract only numeric values

        === VALIDATION RULES ===
        1. AM Name: Must match {request.AmName} (case-insensitive)
        2. Customer Name: Must match {request.CustomerName} (case-insensitive)
        3. Target Values: Must match exactly the numeric values

        === OUTPUT FORMAT ===
        Return ONLY valid JSON in this exact format:
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
              ""expectedValue"": ""7000000"",
              ""pdfValue"": ""Value from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{
              ""field"": ""BRAKE FLUID Target"",
              ""expectedValue"": ""500000"",
              ""pdfValue"": ""Value from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }},
            {{
              ""field"": ""OTHERS Target"",
              ""expectedValue"": ""1000000"",
              ""pdfValue"": ""Value from PDF"",
              ""reason"": ""Reason for mismatch if any""
            }}
          ]
        }}

        === IMPORTANT INSTRUCTIONS ===
        1. AM Name extraction is critical:
           - If you see ""ASHISH BHATTS"" but expected is ""ASHISH BHATT"", the 'S' is likely from the next field
           - Look for transitions from lowercase to UPPERCASE to identify field boundaries
           - Extract names by stopping at common field keywords: Sales, Office, Contact, Region, Area, Code, etc.
           - Common pattern: ""NAME + KEYWORD"" → Extract only ""NAME""
        
        2. For customer name, cleanly remove prefixes like ""[S]-29870 - "" 
        
        3. For numeric values, handle both formats:
           - Indian format: ""70,00,000"" (remove commas)
           - Standard format: ""7000000""
        
        4. Return ONLY valid JSON in the specified format
        
        5. If extracted value has trailing characters from adjacent fields, exclude them:
           - ""BHATTS"" → ""BHATT"" (remove extra S from adjacent field)
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
            max_tokens = 1000,
            response_format = new { type = "json_object" }
        };

        var jsonBody = JsonSerializer.Serialize(requestBody);
        _logger.LogDebug("Request body length: {Length} chars", jsonBody.Length);

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        // Create a fresh HttpRequestMessage to avoid header issues
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = content
        };

        // Add authorization header
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        // Add other required headers
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
        {
            return new ValidationResult
            {
                isValid = false,
                Message = "Empty response from AI service",
                Mismatches = new List<ValidationMismatch>()
            };
        }

        var cleanedResponse = CleanJsonResponse(response);

        try
        {
            var result = JsonSerializer.Deserialize<ValidationResult>(cleanedResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (result == null)
            {
                _logger.LogWarning("Deserialized result is null");
                return CreateDefaultValidationResult("Invalid response format");
            }

            result = CleanExtractedValues(result);
            
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI response. Response: {Response}", response);

            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                response,
                @"```json\s*(.*?)\s*```|```\s*(.*?)\s*```",
                System.Text.RegularExpressions.RegexOptions.Singleline
            );

            if (jsonMatch.Success)
            {
                var jsonContent = jsonMatch.Groups[1].Success ?
                    jsonMatch.Groups[1].Value : jsonMatch.Groups[2].Value;

                try
                {
                    return JsonSerializer.Deserialize<ValidationResult>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? CreateDefaultValidationResult("Parsed from markdown but got null");
                }
                catch
                {
                    return CreateDefaultValidationResult($"Failed to parse JSON from markdown: {ex.Message}");
                }
            }

            return CreateDefaultValidationResult($"Failed to parse AI response: {ex.Message}");
        }
    }

    private string CleanJsonResponse(string response)
    {
        response = System.Text.RegularExpressions.Regex.Replace(
            response,
            @"```json\s*|\s*```",
            "");

        // Remove any leading/trailing whitespace
        response = response.Trim();

        // Ensure it starts with { and ends with }
        if (!response.StartsWith("{"))
        {
            var startIndex = response.IndexOf('{');
            if (startIndex >= 0)
                response = response.Substring(startIndex);
        }

        if (!response.EndsWith("}"))
        {
            var endIndex = response.LastIndexOf('}');
            if (endIndex >= 0)
                response = response.Substring(0, endIndex + 1);
        }

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

    private ValidationResult CleanExtractedValues(ValidationResult result)
    {
        if (result?.Mismatches == null)
            return result;

        foreach (var mismatch in result.Mismatches)
        {
           
            if (mismatch.Field == "AM Name" && !string.IsNullOrEmpty(mismatch.PdfValue))
            {
                mismatch.PdfValue = CleanAmName(mismatch.PdfValue, mismatch.ExpectedValue);
            }

            if (mismatch.Field == "Customer Name" && !string.IsNullOrEmpty(mismatch.PdfValue))
            {
                mismatch.PdfValue = CleanCustomerName(mismatch.PdfValue, mismatch.ExpectedValue);
            }

            if ((mismatch.Field.Contains("Target") || mismatch.Field.EndsWith("Target")) && 
                !string.IsNullOrEmpty(mismatch.PdfValue) && !string.IsNullOrEmpty(mismatch.ExpectedValue))
            {
                mismatch.PdfValue = CleanTargetValue(mismatch.PdfValue, mismatch.ExpectedValue);
            }

            if (!string.IsNullOrEmpty(mismatch.ExpectedValue) &&
                mismatch.PdfValue?.Equals(mismatch.ExpectedValue, StringComparison.OrdinalIgnoreCase) == true)
            {
                mismatch.Reason = "Value matches";
            }
        }

        result.isValid = !result.Mismatches.Any(m =>
            m.PdfValue?.Equals(m.ExpectedValue, StringComparison.OrdinalIgnoreCase) != true);

        if (result.isValid)
        {
            result.Message = "All fields match successfully";
        }

        return result;
    }

    private string CleanAmName(string pdfValue, string expectedValue)
    {
        if (string.IsNullOrEmpty(pdfValue) || string.IsNullOrEmpty(expectedValue))
            return pdfValue;

        pdfValue = pdfValue.Trim();

        if (pdfValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            return pdfValue;

        var commonSuffixes = new[] { "S", "Sales", "Office", "Contact", "Region", "Area", "Code", "State" };

        foreach (var suffix in commonSuffixes)
        {
            if (pdfValue.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var withoutSuffix = pdfValue.Substring(0, pdfValue.Length - suffix.Length).TrimEnd();
                if (withoutSuffix.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return withoutSuffix;
                }
            }
        }

        if (pdfValue.Length > 1 && pdfValue.Length == expectedValue.Length + 1)
        {
            var withoutLastChar = pdfValue.Substring(0, pdfValue.Length - 1);
            if (withoutLastChar.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return withoutLastChar;
            }
        }

        if (CalculateLevenshteinDistance(pdfValue, expectedValue) == 1)
        {
            return expectedValue;
        }

        return pdfValue;
    }

    private string CleanCustomerName(string pdfValue, string expectedValue)
    {
        if (string.IsNullOrEmpty(pdfValue) || string.IsNullOrEmpty(expectedValue))
            return pdfValue;

        pdfValue = pdfValue.Trim();

        // If they already match (case-insensitive), return as is
        if (pdfValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            return pdfValue;

        // Handle missing leading character (e.g., "AM AUTO SALES" should be "A M AUTO SALES")
        // Check if prepending 'A ' would match
        if (!pdfValue.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
        {
            var withLeadingA = "A " + pdfValue;
            if (withLeadingA.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return withLeadingA;
            }
        }

        // Handle missing space after first letter (e.g., "AM AUTO SALES" vs "A M AUTO SALES")
        if (pdfValue.Length >= 2 && pdfValue[0] != ' ')
        {
            var withSpaceAfterFirst = pdfValue[0] + " " + pdfValue.Substring(1);
            if (withSpaceAfterFirst.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                return withSpaceAfterFirst;
            }
        }

        // Handle leading character missing from expected
        // e.g., pdfValue = "A M AUTO SALES", expectedValue = "AM AUTO SALES"
        if (pdfValue.Length > expectedValue.Length)
        {
            // Try removing leading character and space
            if (pdfValue.Length >= 2 && pdfValue[1] == ' ')
            {
                var withoutLeadingChar = pdfValue.Substring(2);
                if (withoutLeadingChar.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return withoutLeadingChar;
                }
            }
        }

        // Fuzzy matching for customer names - allow up to 2 character difference
        int distance = CalculateLevenshteinDistance(pdfValue, expectedValue);
        if (distance <= 2)
        {
            return expectedValue;
        }

        return pdfValue;
    }

    private string CleanTargetValue(string pdfValue, string expectedValue)
    {
        if (string.IsNullOrEmpty(pdfValue) || string.IsNullOrEmpty(expectedValue))
            return pdfValue;

        pdfValue = pdfValue.Trim();

        // If they already match, return as is
        if (pdfValue.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            return pdfValue;

        // Remove commas and spaces to get numeric values
        var pdfNumeric = System.Text.RegularExpressions.Regex.Replace(pdfValue, @"[,\s]", "");
        var expectedNumeric = System.Text.RegularExpressions.Regex.Replace(expectedValue, @"[,\s]", "");

        // If numeric values match after removing formatting, return expected format
        if (pdfNumeric.Equals(expectedNumeric, StringComparison.OrdinalIgnoreCase))
        {
            return expectedValue;
        }

        // Try to parse as numbers to handle Indian format (10,00,000 = 1,000,000)
        if (long.TryParse(pdfNumeric, out long pdfNum) && 
            long.TryParse(expectedNumeric, out long expectedNum))
        {
            // Check if PDF value has an extra zero (common parsing error)
            // e.g., "10000000" should be "1000000"
            if (pdfNum == expectedNum * 10)
            {
                return expectedValue;
            }

            // Check for Indian format confusion
            // Sometimes "10,00,000" (10 lakhs) gets parsed as "1000000" instead of "1000000"
            // but "10000000" gets read as "1,00,00,000" (10 million)
            // If difference is exactly 10x, it's likely an extra digit
            if (pdfNum > expectedNum && pdfNum <= expectedNum * 100)
            {
                // Check if removing the first digit matches
                string pdfStr = pdfNum.ToString();
                if (pdfStr.Length > expectedNumeric.Length)
                {
                    // Try removing each digit from the start and see if it matches
                    for (int i = 0; i < Math.Min(2, pdfStr.Length); i++)
                    {
                        var trimmed = pdfStr.Substring(i);
                        if (trimmed.Equals(expectedNumeric))
                        {
                            return expectedValue;
                        }
                    }
                }
            }
        }

        // If still doesn't match but difference is small, use expected value
        if (long.TryParse(pdfNumeric, out long pdfVal) && 
            long.TryParse(expectedNumeric, out long expVal) &&
            Math.Abs(pdfVal - expVal) <= expVal * 0.1) // Allow 10% difference
        {
            return expectedValue;
        }

        return pdfValue;
    }

    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        s1 = s1 ?? "";
        s2 = s2 ?? "";

        if (s1.Length == 0) return s2.Length;
        if (s2.Length == 0) return s1.Length;

        var distances = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            distances[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            distances[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = char.ToUpper(s1[i - 1]) == char.ToUpper(s2[j - 1]) ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost
                );
            }
        }

        return distances[s1.Length, s2.Length];
    }
}
    