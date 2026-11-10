namespace PrintPool.Api;

/// <summary>Проверка содержимого задания печати.</summary>
public static class JobValidator
{
    private static readonly string[] Types = ["text", "pair", "divider", "qr", "feed", "cut"];
    private static readonly string[] Aligns = ["left", "center", "right"];

    /// <summary>Возвращает текст первой найденной ошибки или null, если задание допустимо.</summary>
    public static string? Validate(ReceiptLineDto[] lines)
    {
        if (lines is null || lines.Length < 1 || lines.Length > 100)
        {
            return "Строк должно быть от 1 до 100.";
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var n = i + 1;
            if (line is null)
            {
                return $"Строка {n}: пустой элемент.";
            }

            if (!Types.Contains(line.Type))
            {
                return $"Строка {n}: неизвестный type.";
            }

            if (line.Align is not null && !Aligns.Contains(line.Align))
            {
                return $"Строка {n}: align должен быть left, center или right.";
            }

            if (line.Size is not null && line.Size != 1 && line.Size != 2)
            {
                return $"Строка {n}: size должен быть 1 или 2.";
            }

            if (line.Text?.Length > 200 || line.Left?.Length > 200 || line.Right?.Length > 200)
            {
                return $"Строка {n}: text, left и right не длиннее 200 символов.";
            }

            if (line.Type == "qr")
            {
                if (string.IsNullOrEmpty(line.Value) || line.Value.Length > 300 || line.Value.Any(c => c > 0x7F))
                {
                    return $"Строка {n}: value у qr обязателен, от 1 до 300 символов ASCII.";
                }
            }

            if (line.Type == "feed" && (line.Lines is null || line.Lines < 1 || line.Lines > 10))
            {
                return $"Строка {n}: lines у feed должен быть от 1 до 10.";
            }
        }

        return null;
    }
}
