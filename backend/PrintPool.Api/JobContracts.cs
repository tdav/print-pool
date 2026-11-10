namespace PrintPool.Api;

/// <summary>Состояние задания печати.</summary>
public enum JobStatus { Queued, Dispatched, Printed, Failed }

/// <summary>Строка чека.</summary>
public sealed record ReceiptLineDto(string Type, string? Text, string? Align, bool? Bold,
    int? Size, string? Left, string? Right, string? Value, int? Lines);

/// <summary>Задание печати.</summary>
public sealed record PrintJob(Guid JobId, Guid DeviceId, ReceiptLineDto[] Lines, JobStatus Status,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt, DateTimeOffset? FinishedAt);

/// <summary>Результат приёма итога печати от устройства.</summary>
public enum ResultOutcome { Accepted, JobNotFound, WrongDevice, WrongState }
