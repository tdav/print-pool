using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PrintPool.Api;

/// <summary>Тело запроса на создание задания.</summary>
public sealed record CreateJobRequest(Guid DeviceId, string? Type, ReceiptLineDto[]? Lines, ReceiptDataDto? Data);

/// <summary>Тело итога печати от устройства.</summary>
public sealed record JobResultRequest(Guid DeviceId, bool? Success, string? Error);

/// <summary>Четыре метода API пула печати.</summary>
public static class JobEndpoints
{
    // Предел тела запроса создания задания.
    private const long MaxBodyBytes = 8192;

    // Предел тела итога печати.
    private const long MaxResultBodyBytes = 2048;

    // Для ответа агенту: пустые поля строк чека не передаются, тело меньше.
    private static readonly JsonSerializerOptions NextJobJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Знаки & ' + < > отдаются плате как есть, а не кодами &: ответ читает плата, а не браузер.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/jobs", (CreateJobRequest? request, JobStore store) =>
        {
            if (request is null || request.DeviceId == Guid.Empty)
            {
                return Results.BadRequest("deviceId обязателен.");
            }

            // Без type задание старого формата со строками.
            var type = request.Type ?? JobValidator.TypeLines;
            if (type != JobValidator.TypeLines && type != JobValidator.TypeKitchen && type != JobValidator.TypeBill)
            {
                return Results.BadRequest("type должен быть lines, kitchen или bill.");
            }

            PrintJob job;
            if (type == JobValidator.TypeLines)
            {
                if (request.Data is not null)
                {
                    return Results.BadRequest("data не допускается при type lines.");
                }

                var error = JobValidator.Validate(request.Lines!);
                if (error is not null)
                {
                    return Results.BadRequest(error);
                }

                job = store.Add(request.DeviceId, type, request.Lines, null);
            }
            else
            {
                if (request.Lines is not null)
                {
                    return Results.BadRequest("lines не допускается при type kitchen и bill.");
                }

                var error = JobValidator.ValidateData(type, request.Data);
                if (error is not null)
                {
                    return Results.BadRequest(error);
                }

                job = store.Add(request.DeviceId, type, null, JobValidator.Normalize(type, request.Data!));
            }

            return Results.Created($"/api/jobs/{job.JobId}", new { jobId = job.JobId, status = job.Status });
        }).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .Produces(StatusCodes.Status201Created)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        app.MapGet("/api/devices/{deviceId:guid}/jobs/next", (Guid deviceId, JobStore store, TimeProvider timeProvider) =>
        {
            var job = store.TakeNext(deviceId);
            // Порядок ключей и их регистр фиксированы: плата разбирает тело с учётом регистра.
            return job is null
                ? Results.NoContent()
                : Results.Json(new
                {
                    jobId = job.JobId,
                    type = job.Type,
                    serverTime = timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                    data = job.Data,
                    lines = job.Lines,
                }, NextJobJson);
        }).Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status204NoContent);

        app.MapPost("/api/jobs/{jobId:guid}/result", (Guid jobId, JobResultRequest? request, JobStore store) =>
        {
            if (request is null || request.DeviceId == Guid.Empty)
            {
                return Results.BadRequest("deviceId обязателен.");
            }

            if (request.Success is null)
            {
                return Results.BadRequest("success обязателен.");
            }

            if (request.Error?.Length > 200)
            {
                return Results.BadRequest("error не длиннее 200 символов.");
            }

            return store.Complete(jobId, request.DeviceId, request.Success.Value, request.Error) switch
            {
                ResultOutcome.Accepted => Results.NoContent(),
                ResultOutcome.JobNotFound => Results.NotFound(),
                _ => Results.Conflict(),
            };
        }).WithMetadata(new RequestSizeLimitAttribute(MaxResultBodyBytes))
            .Produces(StatusCodes.Status204NoContent)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        app.MapGet("/api/jobs/{jobId:guid}", (Guid jobId, JobStore store) =>
        {
            var job = store.Find(jobId);
            return job is null
                ? Results.NotFound()
                : Results.Ok(new
                {
                    jobId = job.JobId,
                    deviceId = job.DeviceId,
                    type = job.Type,
                    data = job.Data,
                    lines = job.Lines,
                    status = job.Status,
                    error = job.Error,
                    createdAt = job.CreatedAt,
                    dispatchedAt = job.DispatchedAt,
                    finishedAt = job.FinishedAt,
                });
        }).Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>Два примера тела POST /api/jobs в Swagger: кухня и счёт.</summary>
public sealed class CreateJobExamplesFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    public void Apply(Microsoft.OpenApi.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        if (operation.RequestBody?.Content is not { } content || !content.TryGetValue("application/json", out var json)
            || context.ApiDescription.RelativePath != "api/jobs" || context.ApiDescription.HttpMethod != "POST")
        {
            return;
        }

        json.Examples = new Dictionary<string, Microsoft.OpenApi.IOpenApiExample>
        {
            ["kitchen"] = new Microsoft.OpenApi.OpenApiExample
            {
                Summary = "Кухня",
                Value = System.Text.Json.Nodes.JsonNode.Parse("""
                    {"deviceId":"3f2a1c9e-0000-0000-0000-000000000000","type":"kitchen","data":{"billNumber":"A20","table":"12","hall":"Siniy zal","cashier":"Vlad","items":[{"name":"SARBAST NEFILTR RAZLIV","quantity":1},{"name":"SHASHLIK MOL GOSHTI","quantity":0.35}]}}
                    """),
            },
            ["bill"] = new Microsoft.OpenApi.OpenApiExample
            {
                Summary = "Счёт",
                Value = System.Text.Json.Nodes.JsonNode.Parse("""
                    {"deviceId":"3f2a1c9e-0000-0000-0000-000000000000","type":"bill","data":{"title":"BEREZKA","billNumber":"A20","table":"12","hall":"Siniy zal","cashier":"Vlad","items":[{"name":"MNOGO MYASA 40SM","quantity":1,"sum":80000},{"name":"SARBAST NEFILTR RAZLIV","quantity":9,"sum":126000}],"subtotal":321000,"servicePercent":10,"serviceSum":32100,"total":353100,"currency":"sum"}}
                    """),
            },
        };
    }
}
