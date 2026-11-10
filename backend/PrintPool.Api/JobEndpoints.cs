using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace PrintPool.Api;

/// <summary>Тело запроса на создание задания.</summary>
public sealed record CreateJobRequest(Guid DeviceId, ReceiptLineDto[]? Lines);

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
    };

    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/jobs", (CreateJobRequest? request, JobStore store) =>
        {
            if (request is null || request.DeviceId == Guid.Empty)
            {
                return Results.BadRequest("deviceId обязателен.");
            }

            var error = JobValidator.Validate(request.Lines!);
            if (error is not null)
            {
                return Results.BadRequest(error);
            }

            var job = store.Add(request.DeviceId, request.Lines!);
            return Results.Created($"/api/jobs/{job.JobId}", new { jobId = job.JobId, status = job.Status });
        }).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .Produces(StatusCodes.Status201Created)
            .Produces<string>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        app.MapGet("/api/devices/{deviceId:guid}/jobs/next", (Guid deviceId, JobStore store) =>
        {
            var job = store.TakeNext(deviceId);
            return job is null
                ? Results.NoContent()
                : Results.Json(new { jobId = job.JobId, lines = job.Lines }, NextJobJson);
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
