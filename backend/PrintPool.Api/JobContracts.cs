namespace PrintPool.Api;

/// <summary>Состояние задания печати.</summary>
public enum JobStatus { Queued, Dispatched, Printed, Failed }

/// <summary>Строка чека.</summary>
public sealed record ReceiptLineDto(string Type, string? Text, string? Align, bool? Bold,
    int? Size, string? Left, string? Right, string? Value, int? Lines);

/// <summary>Позиция чека от отправителя: всё необязательное, проверяет валидатор.</summary>
public sealed record ReceiptItemDto(string? Name, decimal? Quantity, long? Sum);

/// <summary>Данные чека kitchen или bill от отправителя: всё необязательное, проверяет валидатор.</summary>
public sealed record ReceiptDataDto(string? Title, string? BillNumber, string? Table, string? Hall,
    string? Cashier, ReceiptItemDto[]? Items, long? Subtotal, int? ServicePercent, long? ServiceSum,
    long? Total, string? Currency);

/// <summary>Позиция чека для платы: количество готовой печатной строкой.</summary>
public sealed record ReceiptItem(string Name, string Quantity, long? Sum);

/// <summary>Данные чека для платы: без null, кроме полей счёта у kitchen (они не сериализуются).</summary>
public sealed record ReceiptData(string? Title, string BillNumber, string Table, string Hall, string Cashier,
    ReceiptItem[] Items, long? Subtotal, int? ServicePercent, long? ServiceSum, long? Total, string? Currency);

/// <summary>Задание печати: Lines заполнено при Type == lines, иначе Data.</summary>
public sealed record PrintJob(Guid JobId, Guid DeviceId, string Type, ReceiptLineDto[]? Lines, ReceiptData? Data, JobStatus Status,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt, DateTimeOffset? FinishedAt);

/// <summary>Результат приёма итога печати от устройства.</summary>
public enum ResultOutcome { Accepted, JobNotFound, WrongDevice, WrongState }
