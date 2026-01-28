using PdfTargetValidator.Interfaces;
using PdfTargetValidator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpClient with configuration for OpenAI
builder.Services.AddHttpClient<ILlmService, LlmService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Register your services
builder.Services.AddScoped<ILlmService, LlmService>();
builder.Services.AddScoped<IPdfService, PdfService>();
// Add other services...

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();