namespace PrintPool.Api.Tests;

public class JobValidatorTests
{
    private static ReceiptLineDto Line(string type, string? text = null, string? align = null, int? size = null,
        string? left = null, string? right = null, string? value = null, int? lines = null)
        => new(type, text, align, null, size, left, right, value, lines);

    [Fact]
    public void ValidJobFromSpec_ReturnsNull()
    {
        ReceiptLineDto[] lines =
        [
            new("text", "Cafe", "center", true, 2, null, null, null, null),
            new("divider", null, null, null, null, null, null, null, null),
            new("pair", null, null, null, null, "Latte", "250", null, null),
            new("qr", null, "center", null, null, null, null, "https://example.com/r/1", null),
            new("feed", null, null, null, null, null, null, null, 3),
            new("cut", null, null, null, null, null, null, null, null),
        ];
        Assert.Null(JobValidator.Validate(lines));
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
}
