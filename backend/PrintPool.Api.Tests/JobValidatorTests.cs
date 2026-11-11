using System.Text.Json;

namespace PrintPool.Api.Tests;

public class JobValidatorTests
{
    private static ReceiptLineDto Line(string type, string? text = null, string? align = null, int? size = null,
        string? left = null, string? right = null, string? value = null, int? lines = null)
        => new(type, text, align, null, size, left, right, value, lines);

    // Задание из спеки дословно.
    public const string SpecJob = """
        {
          "deviceId": "3f2a9c1e-0000-0000-0000-000000000000",
          "lines": [
            { "type": "text", "text": "CAFE ROMASHKA", "align": "center", "bold": true, "size": 2 },
            { "type": "divider" },
            { "type": "pair", "left": "Plov 1 x 35 000", "right": "35 000" },
            { "type": "pair", "left": "TOTAL", "right": "45 000", "bold": true },
            { "type": "qr", "value": "https://example.com/c/1045" },
            { "type": "feed", "lines": 3 },
            { "type": "cut" }
          ]
        }
        """;

    [Fact]
    public void ValidJobFromSpec_ReturnsNull()
    {
        var job = JsonSerializer.Deserialize<CreateJobRequest>(SpecJob, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(JobValidator.Validate(job!.Lines!));
    }

    [Fact]
    public void NullLines_ReturnsError() => Assert.NotNull(JobValidator.Validate(null!));

    [Fact]
    public void EmptyLines_ReturnsError() => Assert.NotNull(JobValidator.Validate([]));

    [Fact]
    public void NullLineElement_ReturnsError() => Assert.NotNull(JobValidator.Validate([null!]));

    [Fact]
    public void OneHundredLines_ReturnsNull()
        => Assert.Null(JobValidator.Validate(Enumerable.Range(0, 100).Select(_ => Line("cut")).ToArray()));

    [Fact]
    public void MoreThan100Lines_ReturnsError()
        => Assert.NotNull(JobValidator.Validate(Enumerable.Range(0, 101).Select(_ => Line("cut")).ToArray()));

    [Fact]
    public void UnknownType_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("image")]));

    [Fact]
    public void NullType_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line(null!)]));

    [Theory]
    [InlineData("left")]
    [InlineData("center")]
    [InlineData("right")]
    public void KnownAlign_ReturnsNull(string align) => Assert.Null(JobValidator.Validate([Line("text", "a", align)]));

    [Fact]
    public void UnknownAlign_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("text", "a", "middle")]));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void KnownSize_ReturnsNull(int size) => Assert.Null(JobValidator.Validate([Line("text", "a", size: size)]));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void UnknownSize_ReturnsError(int size) => Assert.NotNull(JobValidator.Validate([Line("text", "a", size: size)]));

    [Fact]
    public void Text200Chars_ReturnsNull() => Assert.Null(JobValidator.Validate([Line("text", new string('a', 200))]));

    [Fact]
    public void Text201Chars_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("text", new string('a', 201))]));

    [Fact]
    public void Left201Chars_ReturnsError()
        => Assert.NotNull(JobValidator.Validate([Line("pair", left: new string('a', 201), right: "1")]));

    [Fact]
    public void Right201Chars_ReturnsError()
        => Assert.NotNull(JobValidator.Validate([Line("pair", left: "a", right: new string('a', 201))]));

    [Fact]
    public void QrWithoutValue_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("qr")]));

    [Fact]
    public void QrEmptyValue_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("qr", value: "")]));

    [Fact]
    public void Qr300Chars_ReturnsNull() => Assert.Null(JobValidator.Validate([Line("qr", value: new string('a', 300))]));

    [Fact]
    public void Qr301Chars_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("qr", value: new string('a', 301))]));

    [Fact]
    public void QrNonAscii_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("qr", value: "привет")]));

    [Fact]
    public void FeedWithoutLines_ReturnsError() => Assert.NotNull(JobValidator.Validate([Line("feed")]));

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void FeedOutOfRange_ReturnsError(int lines) => Assert.NotNull(JobValidator.Validate([Line("feed", lines: lines)]));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void FeedInRange_ReturnsNull(int lines) => Assert.Null(JobValidator.Validate([Line("feed", lines: lines)]));

    // ---- Типы чеков kitchen и bill ----

    private static ReceiptItemDto Item(string? name = "A", decimal? quantity = 1m, long? sum = 100)
        => new(name, quantity, sum);

    private static ReceiptDataDto Data(string? billNumber = "A20", string? table = "12", string? hall = "Hall",
        string? cashier = "Vlad", ReceiptItemDto[]? items = null, long? subtotal = 100, int? servicePercent = null,
        long? serviceSum = null, long? total = 100, string? title = null, string? currency = null)
        => new(title, billNumber, table, hall, cashier, items ?? [Item()], subtotal, servicePercent, serviceSum, total, currency);

    private static decimal Dec(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_Null_ReturnsError(string type) => Assert.NotNull(JobValidator.ValidateData(type, null));

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_Valid_ReturnsNull(string type) => Assert.Null(JobValidator.ValidateData(type, Data()));

    [Fact]
    public void ValidateData_UnknownType_ReturnsError() => Assert.NotNull(JobValidator.ValidateData("lines", Data()));

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_BillNumberBounds(string type)
    {
        Assert.NotNull(JobValidator.ValidateData(type, Data(billNumber: null)));
        Assert.NotNull(JobValidator.ValidateData(type, Data(billNumber: "")));
        Assert.Null(JobValidator.ValidateData(type, Data(billNumber: "a")));
        Assert.Null(JobValidator.ValidateData(type, Data(billNumber: new string('a', 20))));
        Assert.Contains("billNumber", JobValidator.ValidateData(type, Data(billNumber: new string('a', 21))));
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_TableBounds(string type)
    {
        Assert.NotNull(JobValidator.ValidateData(type, Data(table: null)));
        Assert.NotNull(JobValidator.ValidateData(type, Data(table: "")));
        Assert.Null(JobValidator.ValidateData(type, Data(table: "a")));
        Assert.Null(JobValidator.ValidateData(type, Data(table: new string('a', 20))));
        Assert.Contains("table", JobValidator.ValidateData(type, Data(table: new string('a', 21))));
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_HallBounds(string type)
    {
        Assert.Null(JobValidator.ValidateData(type, Data(hall: null)));
        Assert.Null(JobValidator.ValidateData(type, Data(hall: "")));
        Assert.Null(JobValidator.ValidateData(type, Data(hall: new string('a', 40))));
        Assert.Contains("hall", JobValidator.ValidateData(type, Data(hall: new string('a', 41))));
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_CashierBounds(string type)
    {
        Assert.NotNull(JobValidator.ValidateData(type, Data(cashier: null)));
        Assert.NotNull(JobValidator.ValidateData(type, Data(cashier: "")));
        Assert.Null(JobValidator.ValidateData(type, Data(cashier: "a")));
        Assert.Null(JobValidator.ValidateData(type, Data(cashier: new string('a', 40))));
        Assert.Contains("cashier", JobValidator.ValidateData(type, Data(cashier: new string('a', 41))));
    }

    [Fact]
    public void ValidateData_TitleBounds_Bill()
    {
        Assert.Null(JobValidator.ValidateData("bill", Data(title: null)));
        Assert.Null(JobValidator.ValidateData("bill", Data(title: "")));
        Assert.Null(JobValidator.ValidateData("bill", Data(title: new string('a', 24))));
        Assert.Contains("title", JobValidator.ValidateData("bill", Data(title: new string('a', 25))));
    }

    [Fact]
    public void ValidateData_Title_IgnoredForKitchen()
        => Assert.Null(JobValidator.ValidateData("kitchen", Data(title: new string('a', 25))));

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_ItemsCountBounds(string type)
    {
        Assert.Contains("items", JobValidator.ValidateData(type, Data() with { Items = null })!);
        Assert.Contains("items", JobValidator.ValidateData(type, Data(items: []))!);
        Assert.Null(JobValidator.ValidateData(type, Data(items: [Item()])));
        Assert.Null(JobValidator.ValidateData(type, Data(items: Enumerable.Range(0, 60).Select(_ => Item()).ToArray())));
        Assert.Contains("items", JobValidator.ValidateData(type, Data(items: Enumerable.Range(0, 61).Select(_ => Item()).ToArray()))!);
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_NullItemElement_ReturnsError(string type)
        => Assert.NotNull(JobValidator.ValidateData(type, Data(items: [null!])));

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_ItemNameBounds(string type)
    {
        Assert.NotNull(JobValidator.ValidateData(type, Data(items: [Item(name: null)])));
        Assert.NotNull(JobValidator.ValidateData(type, Data(items: [Item(name: "")])));
        Assert.Null(JobValidator.ValidateData(type, Data(items: [Item(name: "a")])));
        Assert.Null(JobValidator.ValidateData(type, Data(items: [Item(name: new string('a', 100))])));
        Assert.Contains("name", JobValidator.ValidateData(type, Data(items: [Item(name: new string('a', 101))])));
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_QuantityMissing_ReturnsError(string type)
        => Assert.Contains("quantity", JobValidator.ValidateData(type, Data(items: [Item(quantity: null)])));

    [Theory]
    [InlineData("0.001")]
    [InlineData("0.35")]
    [InlineData("1")]
    [InlineData("99.99")]
    [InlineData("9999")]
    [InlineData("0.3500")]
    public void ValidateData_QuantityValid_ReturnsNull(string quantity)
    {
        Assert.Null(JobValidator.ValidateData("kitchen", Data(items: [Item(quantity: Dec(quantity))])));
        Assert.Null(JobValidator.ValidateData("bill", Data(items: [Item(quantity: Dec(quantity))])));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10000")]
    [InlineData("0.1234")]
    [InlineData("12.125")]
    [InlineData("9999.5")]
    [InlineData("0.0004")]
    [InlineData("999.99")]
    public void ValidateData_QuantityInvalid_ReturnsError(string quantity)
    {
        Assert.Contains("quantity", JobValidator.ValidateData("kitchen", Data(items: [Item(quantity: Dec(quantity))])));
        Assert.Contains("quantity", JobValidator.ValidateData("bill", Data(items: [Item(quantity: Dec(quantity))])));
    }

    [Theory]
    [InlineData("kitchen")]
    [InlineData("bill")]
    public void ValidateData_WhitespaceOnlyRequiredStrings_ReturnError(string type)
    {
        Assert.Contains("billNumber", JobValidator.ValidateData(type, Data(billNumber: "   ")));
        Assert.Contains("table", JobValidator.ValidateData(type, Data(table: "  ")));
        Assert.Contains("cashier", JobValidator.ValidateData(type, Data(cashier: " ")));
        Assert.Contains("name", JobValidator.ValidateData(type, Data(items: [Item(name: "   ")])));
    }

    [Fact]
    public void ValidateData_ItemSum_RequiredAndBoundedForBill()
    {
        Assert.Contains("sum", JobValidator.ValidateData("bill", Data(items: [Item(sum: null)])));
        Assert.Contains("sum", JobValidator.ValidateData("bill", Data(items: [Item(sum: -1)])));
        Assert.Null(JobValidator.ValidateData("bill", Data(items: [Item(sum: 0)])));
        Assert.Null(JobValidator.ValidateData("bill", Data(items: [Item(sum: 999_999_999)])));
        Assert.Contains("sum", JobValidator.ValidateData("bill", Data(items: [Item(sum: 1_000_000_000)])));
    }

    [Fact]
    public void ValidateData_Subtotal_RequiredAndBoundedForBill()
    {
        Assert.StartsWith("subtotal ", JobValidator.ValidateData("bill", Data(subtotal: null)));
        Assert.StartsWith("subtotal ", JobValidator.ValidateData("bill", Data(subtotal: -1)));
        Assert.Null(JobValidator.ValidateData("bill", Data(subtotal: 0)));
        Assert.Null(JobValidator.ValidateData("bill", Data(subtotal: 999_999_999)));
        Assert.StartsWith("subtotal ", JobValidator.ValidateData("bill", Data(subtotal: 1_000_000_000)));
    }

    [Fact]
    public void ValidateData_Total_RequiredAndBoundedForBill()
    {
        Assert.StartsWith("total ", JobValidator.ValidateData("bill", Data(total: null)));
        Assert.StartsWith("total ", JobValidator.ValidateData("bill", Data(total: -1)));
        Assert.Null(JobValidator.ValidateData("bill", Data(total: 0)));
        Assert.Null(JobValidator.ValidateData("bill", Data(total: 999_999_999)));
        Assert.StartsWith("total ", JobValidator.ValidateData("bill", Data(total: 1_000_000_000)));
    }

    [Fact]
    public void ValidateData_ServicePercentBounds_Bill()
    {
        Assert.Null(JobValidator.ValidateData("bill", Data(servicePercent: null)));
        Assert.Null(JobValidator.ValidateData("bill", Data(servicePercent: 0)));
        Assert.Null(JobValidator.ValidateData("bill", Data(servicePercent: 100)));
        Assert.Contains("servicePercent", JobValidator.ValidateData("bill", Data(servicePercent: -1)));
        Assert.Contains("servicePercent", JobValidator.ValidateData("bill", Data(servicePercent: 101)));
    }

    [Fact]
    public void ValidateData_ServiceSumBounds_Bill()
    {
        Assert.Null(JobValidator.ValidateData("bill", Data(serviceSum: null)));
        Assert.Null(JobValidator.ValidateData("bill", Data(serviceSum: 0)));
        Assert.Null(JobValidator.ValidateData("bill", Data(serviceSum: 999_999_999)));
        Assert.Contains("serviceSum", JobValidator.ValidateData("bill", Data(serviceSum: -1)));
        Assert.Contains("serviceSum", JobValidator.ValidateData("bill", Data(serviceSum: 1_000_000_000)));
    }

    [Fact]
    public void ValidateData_CurrencyBounds_Bill()
    {
        Assert.Null(JobValidator.ValidateData("bill", Data(currency: null)));
        Assert.Null(JobValidator.ValidateData("bill", Data(currency: new string('a', 8))));
        Assert.Contains("currency", JobValidator.ValidateData("bill", Data(currency: new string('a', 9))));
    }

    [Fact]
    public void ValidateData_Kitchen_IgnoresBillFields()
    {
        var data = Data(items: [Item(sum: -5)], subtotal: null, total: -1, servicePercent: 500, serviceSum: -1,
            currency: new string('a', 50));
        Assert.Null(JobValidator.ValidateData("kitchen", data));
    }

    [Fact]
    public void Normalize_Kitchen_DropsBillFields()
    {
        var data = Data(items: [Item(sum: 500)], subtotal: 1, total: 2, servicePercent: 10, serviceSum: 3,
            title: "T", currency: "sum");
        var r = JobValidator.Normalize("kitchen", data);

        Assert.Null(r.Title);
        Assert.Null(r.Currency);
        Assert.Null(r.Subtotal);
        Assert.Null(r.ServicePercent);
        Assert.Null(r.ServiceSum);
        Assert.Null(r.Total);
        Assert.Null(r.Items[0].Sum);
        Assert.Equal("A20", r.BillNumber);
        Assert.Equal("1", r.Items[0].Quantity);
    }

    [Fact]
    public void Normalize_Bill_FillsDefaults()
    {
        var r = JobValidator.Normalize("bill", Data(hall: null, title: null, currency: null, servicePercent: null,
            serviceSum: null));

        Assert.Equal("", r.Hall);
        Assert.Equal("", r.Title);
        Assert.Equal("", r.Currency);
        Assert.Equal(0, r.ServicePercent);
        Assert.Equal(0L, r.ServiceSum);
    }

    [Fact]
    public void Normalize_Kitchen_NullHall_BecomesEmpty()
        => Assert.Equal("", JobValidator.Normalize("kitchen", Data(hall: null)).Hall);

    [Fact]
    public void Normalize_Bill_KeepsValues()
    {
        var r = JobValidator.Normalize("bill", Data(items: [Item(name: "X", quantity: 0.350m, sum: 80000)],
            subtotal: 321000, servicePercent: 10, serviceSum: 32100, total: 353100, title: "BEREZKA", currency: "sum"));

        Assert.Equal("BEREZKA", r.Title);
        Assert.Equal("X", r.Items[0].Name);
        Assert.Equal("0,35", r.Items[0].Quantity);
        Assert.Equal(80000L, r.Items[0].Sum);
        Assert.Equal(321000L, r.Subtotal);
        Assert.Equal(10, r.ServicePercent);
        Assert.Equal(32100L, r.ServiceSum);
        Assert.Equal(353100L, r.Total);
        Assert.Equal("sum", r.Currency);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1.0", "1")]
    [InlineData("0.5", "0,5")]
    [InlineData("0.3500", "0,35")]
    [InlineData("0.350", "0,35")]
    [InlineData("9999", "9999")]
    [InlineData("99.99", "99,99")]
    public void FormatQuantity_Cases(string quantity, string expected)
        => Assert.Equal(expected, JobValidator.FormatQuantity(Dec(quantity)));
}
