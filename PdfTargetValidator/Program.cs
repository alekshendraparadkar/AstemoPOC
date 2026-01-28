using DotNetEnv;
using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Services;

Env.Load();  // Load .env file

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();

// Register HttpClient for OpenAI
builder.Services.AddHttpClient<ILlmService, LlmService>((serviceProvider, client) =>
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (string.IsNullOrEmpty(apiKey))
        throw new InvalidOperationException("OPENAI_API_KEY not found in .env file");

    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
});

// Register PDF service
builder.Services.AddScoped<IPdfService, PdfService>();

var app = builder.Build();

// Configure pipeline
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();