using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PrintPool.Api;

var builder = WebApplication.CreateBuilder(args);

// Адрес по умолчанию; не трогаем, если он задан снаружи (аргументы, окружение, тесты).
if (string.IsNullOrEmpty(builder.Configuration["urls"]) && string.IsNullOrEmpty(builder.Configuration["http_ports"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5080");
}

var timeoutSeconds = builder.Configuration.GetValue("PrintPool:DispatchTimeoutSeconds", 60);
if (timeoutSeconds < 1)
{
    throw new InvalidOperationException(
        $"PrintPool:DispatchTimeoutSeconds должно быть не меньше 1, получено {timeoutSeconds}.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new JobStore(sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(timeoutSeconds)));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Кириллица в текстах ошибок не экранируется.
    o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.OperationFilter<CreateJobExamplesFilter>());

var app = builder.Build();

// Swagger включён во всех средах: проект тестовый.
app.UseSwagger();
app.UseSwaggerUI();
app.MapJobEndpoints();
app.Run();

/// <summary>Нужен тестовому проекту для WebApplicationFactory.</summary>
public partial class Program { }
