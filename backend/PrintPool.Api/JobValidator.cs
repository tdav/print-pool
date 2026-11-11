namespace PrintPool.Api;

/// <summary>Проверка содержимого задания печати.</summary>
public static class JobValidator
{
    public const string TypeLines = "lines", TypeKitchen = "kitchen", TypeBill = "bill";

    private const long MaxSum = 999_999_999;

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

    /// <summary>Возвращает текст первой ошибки данных чека или null, если данные допустимы.</summary>
    public static string? ValidateData(string type, ReceiptDataDto? data)
    {
        if (type != TypeKitchen && type != TypeBill)
        {
            return "type должен быть kitchen или bill.";
        }

        if (data is null)
        {
            return "data обязательно.";
        }

        var bill = type == TypeBill;
        if (string.IsNullOrWhiteSpace(data.BillNumber) || data.BillNumber.Length > 20)
        {
            return "billNumber обязателен, от 1 до 20 знаков.";
        }

        if (string.IsNullOrWhiteSpace(data.Table) || data.Table.Length > 20)
        {
            return "table обязателен, от 1 до 20 знаков.";
        }

        if (data.Hall?.Length > 40)
        {
            return "hall не длиннее 40 знаков.";
        }

        if (string.IsNullOrWhiteSpace(data.Cashier) || data.Cashier.Length > 40)
        {
            return "cashier обязателен, от 1 до 40 знаков.";
        }

        if (bill && data.Title?.Length > 24)
        {
            return "title не длиннее 24 знаков.";
        }

        if (data.Items is null || data.Items.Length < 1 || data.Items.Length > 60)
        {
            return "items обязателен, от 1 до 60 позиций.";
        }

        for (var i = 0; i < data.Items.Length; i++)
        {
            var item = data.Items[i];
            var n = i + 1;
            if (item is null)
            {
                return $"items[{n}]: пустой элемент.";
            }

            if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 100)
            {
                return $"items[{n}].name обязателен, от 1 до 100 знаков.";
            }

            if (item.Quantity is not { } q || q <= 0 || q > 9999 || Math.Round(q, 3) != q)
            {
                return $"items[{n}].quantity обязателен: больше 0, не больше 9999, не более трёх знаков после запятой.";
            }

            // Колонка количества в счёте — 5 знаков.
            if (FormatQuantity(q).Length > 5)
            {
                return $"items[{n}].quantity в печатном виде не длиннее 5 знаков.";
            }

            if (bill && (item.Sum is null || item.Sum < 0 || item.Sum > MaxSum))
            {
                return $"items[{n}].sum обязателен, от 0 до 999999999.";
            }
        }

        if (!bill)
        {
            return null;
        }

        if (data.Subtotal is null || data.Subtotal < 0 || data.Subtotal > MaxSum)
        {
            return "subtotal обязателен, от 0 до 999999999.";
        }

        if (data.Total is null || data.Total < 0 || data.Total > MaxSum)
        {
            return "total обязателен, от 0 до 999999999.";
        }

        if (data.ServicePercent is < 0 or > 100)
        {
            return "servicePercent должен быть от 0 до 100.";
        }

        if (data.ServiceSum is < 0 or > MaxSum)
        {
            return "serviceSum должен быть от 0 до 999999999.";
        }

        if (data.Currency?.Length > 8)
        {
            return "currency не длиннее 8 знаков.";
        }

        return null;
    }

    /// <summary>Вид данных для платы; вызывать только после ValidateData == null.</summary>
    public static ReceiptData Normalize(string type, ReceiptDataDto data)
    {
        var bill = type == TypeBill;
        var items = data.Items!.Select(x => new ReceiptItem(x.Name!, FormatQuantity(x.Quantity!.Value), bill ? x.Sum : null)).ToArray();
        return new ReceiptData(
            bill ? data.Title ?? "" : null,
            data.BillNumber!,
            data.Table!,
            data.Hall ?? "",
            data.Cashier!,
            items,
            bill ? data.Subtotal : null,
            bill ? data.ServicePercent ?? 0 : null,
            bill ? data.ServiceSum ?? 0 : null,
            bill ? data.Total : null,
            bill ? data.Currency ?? "" : null);
    }

    /// <summary>Печатный вид количества: без лишних нулей, запятая вместо точки.</summary>
    public static string FormatQuantity(decimal quantity)
        => quantity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
