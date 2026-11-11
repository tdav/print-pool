using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace PrintPool.Api.Tests;

public class JobEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TextBody = """{"type":"text","text":"hi"}""";

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string JobJson(Guid deviceId, string line = TextBody)
        => $$"""{"deviceId":"{{deviceId}}","lines":[{{line}}]}""";

    private static string ResultJson(Guid deviceId, bool success = true)
        => $$"""{"deviceId":"{{deviceId}}","success":{{(success ? "true" : "false")}}}""";

    [Fact]
    public async Task PostJob_Valid_Returns201WithJobId()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(JobJson(Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("jobId").GetString(), out _));
        Assert.Equal("Queued", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostJob_UnknownLineType_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(JobJson(Guid.NewGuid(), """{"type":"image"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_EmptyLines_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json($$"""{"deviceId":"{{Guid.NewGuid()}}","lines":[]}"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_EmptyDeviceId_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(JobJson(Guid.Empty)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_InvalidJson_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json("{not json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_BodyOver8Kb_Returns413()
    {
        // Предел тела применяет Kestrel; TestServer его не поддерживает, поэтому поднимаем настоящий сервер на свободном порту.
        using var kestrelFactory = new WebApplicationFactory<Program>();
        kestrelFactory.UseKestrel(0);
        var client = kestrelFactory.CreateClient();
        // Тело чуть больше 8192 байт; строки по 200 символов валидны, значит 413 даёт именно предел тела.
        var line = $$"""{"type":"text","text":"{{new string('a', 200)}}"}""";
        var json = JobJson(Guid.NewGuid(), string.Join(",", Enumerable.Repeat(line, 36)));
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 8193, 8600);
        var response = await client.PostAsync("/api/jobs", Json(json));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task GetNext_NoJobs_Returns204()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/devices/{Guid.NewGuid()}/jobs/next");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetNext_OmitsNullFields()
    {
        var client = factory.CreateClient();
        var device = Guid.NewGuid();
        await client.PostAsync("/api/jobs", Json(JobJson(device)));
        var text = await client.GetStringAsync($"/api/devices/{device}/jobs/next");
        Assert.Contains("\"lines\":[{\"type\":\"text\",\"text\":\"hi\"}]", text);
    }

    [Fact]
    public async Task FullCycle_AddTakeCompleteStatus()
    {
        var client = factory.CreateClient();
        var device = Guid.NewGuid();
        var post = await client.PostAsync("/api/jobs", Json(JobJson(device)));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var jobId = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();

        var next = await client.GetAsync($"/api/devices/{device}/jobs/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(jobId, (await next.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString());

        var result = await client.PostAsync($"/api/jobs/{jobId}/result", Json(ResultJson(device)));
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);

        var after = await client.GetAsync($"/api/devices/{device}/jobs/next");
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{jobId}");
        Assert.Equal("Printed", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetJob_Unknown_Returns404()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostResult_WrongDevice_Returns409()
    {
        var client = factory.CreateClient();
        var device = Guid.NewGuid();
        var post = await client.PostAsync("/api/jobs", Json(JobJson(device)));
        var jobId = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();
        await client.GetAsync($"/api/devices/{device}/jobs/next");
        var result = await client.PostAsync($"/api/jobs/{jobId}/result", Json(ResultJson(Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task PostResult_UnknownJob_Returns404()
    {
        var client = factory.CreateClient();
        var result = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json(ResultJson(Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task PostResult_Repeated_Returns204()
    {
        var client = factory.CreateClient();
        var device = Guid.NewGuid();
        var post = await client.PostAsync("/api/jobs", Json(JobJson(device)));
        var jobId = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();
        await client.GetAsync($"/api/devices/{device}/jobs/next");
        await client.PostAsync($"/api/jobs/{jobId}/result", Json(ResultJson(device)));
        var again = await client.PostAsync($"/api/jobs/{jobId}/result", Json(ResultJson(device)));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [Fact]
    public async Task PostResult_EmptyDeviceId_Returns400()
    {
        var client = factory.CreateClient();
        var result = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json("""{"success":true}"""));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task PostResult_InvalidJson_Returns400()
    {
        var client = factory.CreateClient();
        var result = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json("nope"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Swagger_Json_IsServed()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_SpecJob_Returns201()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(JobValidatorTests.SpecJob));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostResult_BodyOver2Kb_Returns413()
    {
        using var kestrelFactory = new WebApplicationFactory<Program>();
        kestrelFactory.UseKestrel(0);
        var client = kestrelFactory.CreateClient();
        var json = $$"""{"deviceId":"{{Guid.NewGuid()}}","success":false,"error":"{{new string('a', 2100)}}"}""";
        var response = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json(json));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PostResult_ErrorOver200Chars_Returns400()
    {
        var client = factory.CreateClient();
        var json = $$"""{"deviceId":"{{Guid.NewGuid()}}","success":false,"error":"{{new string('a', 201)}}"}""";
        var response = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json(json));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostResult_MissingSuccess_Returns400()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/jobs/{Guid.NewGuid()}/result", Json($$"""{"deviceId":"{{Guid.NewGuid()}}"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_DeclaresResponseCodes()
    {
        var client = factory.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = doc.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/jobs").GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(paths.GetProperty("/api/jobs/{jobId}/result").GetProperty("post").GetProperty("responses").TryGetProperty("409", out _));
    }

    [Fact]
    public async Task PostJob_Error400_BodyKeepsCyrillicUnescaped()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(JobJson(Guid.NewGuid(), """{"type":"image"}""")));
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("неизвестный", raw);
    }

    [Fact]
    public void Startup_TimeoutZero_FailsWithClearException()
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("PrintPool:DispatchTimeoutSeconds", "0"));
        var ex = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
        Assert.Contains("DispatchTimeoutSeconds", ex.ToString());
    }

    // ---- Типы чеков kitchen и bill ----

    // Тела из спеки дословно.
    private const string KitchenBody = """
        {
          "deviceId": "{DEVICE}",
          "type": "kitchen",
          "data": {
            "billNumber": "A20",
            "table": "12",
            "hall": "Siniy zal",
            "cashier": "Vlad",
            "items": [
              { "name": "SARBAST NEFILTR RAZLIV", "quantity": 1 },
              { "name": "SHASHLIK MOL GOSHTI", "quantity": 0.35 }
            ]
          }
        }
        """;

    private const string BillBody = """
        {
          "deviceId": "{DEVICE}",
          "type": "bill",
          "data": {
            "title": "BEREZKA",
            "billNumber": "A20",
            "table": "12",
            "hall": "Siniy zal",
            "cashier": "Vlad",
            "items": [
              { "name": "MNOGO MYASA 40SM", "quantity": 1, "sum": 80000 },
              { "name": "SARBAST NEFILTR RAZLIV", "quantity": 9, "sum": 126000 }
            ],
            "subtotal": 321000,
            "servicePercent": 10,
            "serviceSum": 32100,
            "total": 353100,
            "currency": "sum"
          }
        }
        """;

    // 2026-11-11T17:53:00Z, в секундах от 1970 года это 1794419580.
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 17, 53, 0, TimeSpan.Zero);

    private HttpClient ClientAt(FakeTimeProvider time)
        => factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(time))).CreateClient();

    private static async Task<string> PostAndGetNextAsync(HttpClient client, string body, Guid device)
    {
        var post = await client.PostAsync("/api/jobs", Json(body.Replace("{DEVICE}", device.ToString())));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        return await client.GetStringAsync($"/api/devices/{device}/jobs/next");
    }

    [Fact]
    public async Task GetNext_Kitchen_BodyHasExactKeysAndStringQuantity()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        var text = await PostAndGetNextAsync(client, KitchenBody, Guid.NewGuid());
        var jobId = JsonDocument.Parse(text).RootElement.GetProperty("jobId").GetString();

        // Плата разбирает ключи с учётом регистра, поэтому сверяем текст тела, а не десериализацию.
        Assert.Equal(
            $$$"""{"jobId":"{{{jobId}}}","type":"kitchen","serverTime":1794419580,"data":{"billNumber":"A20","table":"12","hall":"Siniy zal","cashier":"Vlad","items":[{"name":"SARBAST NEFILTR RAZLIV","quantity":"1"},{"name":"SHASHLIK MOL GOSHTI","quantity":"0,35"}]}}""",
            text);
    }

    [Fact]
    public async Task GetNext_Bill_BodyHasExactKeysAndStringQuantity()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        var text = await PostAndGetNextAsync(client, BillBody, Guid.NewGuid());
        var jobId = JsonDocument.Parse(text).RootElement.GetProperty("jobId").GetString();

        Assert.Equal(
            $$$"""{"jobId":"{{{jobId}}}","type":"bill","serverTime":1794419580,"data":{"title":"BEREZKA","billNumber":"A20","table":"12","hall":"Siniy zal","cashier":"Vlad","items":[{"name":"MNOGO MYASA 40SM","quantity":"1","sum":80000},{"name":"SARBAST NEFILTR RAZLIV","quantity":"9","sum":126000}],"subtotal":321000,"servicePercent":10,"serviceSum":32100,"total":353100,"currency":"sum"}}""",
            text);
    }

    [Fact]
    public async Task GetNext_Kitchen_ParsedValues()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        var text = await PostAndGetNextAsync(client, KitchenBody, Guid.NewGuid());
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("kitchen", root.GetProperty("type").GetString());
        Assert.Equal(1794419580L, root.GetProperty("serverTime").GetInt64());
        var data = root.GetProperty("data");
        Assert.Equal(JsonValueKind.String, data.GetProperty("items")[1].GetProperty("quantity").ValueKind);
        Assert.Equal("0,35", data.GetProperty("items")[1].GetProperty("quantity").GetString());
        Assert.False(root.TryGetProperty("lines", out _));
        Assert.False(data.TryGetProperty("title", out _));
        Assert.False(data.TryGetProperty("total", out _));
        Assert.False(data.GetProperty("items")[0].TryGetProperty("sum", out _));
    }

    [Fact]
    public async Task GetNext_BillWithoutHallAndCurrency_DefaultsAreEmptyAndZero()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        const string body = """
            {"deviceId":"{DEVICE}","type":"bill","data":{"billNumber":"A1","table":"1","cashier":"V",
            "items":[{"name":"A","quantity":1,"sum":10}],"subtotal":10,"total":10}}
            """;
        var data = JsonDocument.Parse(await PostAndGetNextAsync(client, body, Guid.NewGuid())).RootElement.GetProperty("data");
        Assert.Equal("", data.GetProperty("hall").GetString());
        Assert.Equal("", data.GetProperty("currency").GetString());
        Assert.Equal("", data.GetProperty("title").GetString());
        Assert.Equal(0, data.GetProperty("servicePercent").GetInt32());
        Assert.Equal(0, data.GetProperty("serviceSum").GetInt32());
    }

    [Fact]
    public async Task GetNext_LinesWithoutType_HasTypeLinesAndNoData()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        var device = Guid.NewGuid();
        await client.PostAsync("/api/jobs", Json(JobJson(device)));
        var text = await client.GetStringAsync($"/api/devices/{device}/jobs/next");
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("lines", root.GetProperty("type").GetString());
        Assert.Equal(1794419580L, root.GetProperty("serverTime").GetInt64());
        Assert.Contains("\"lines\":[{\"type\":\"text\",\"text\":\"hi\"}]", text);
        Assert.False(root.TryGetProperty("data", out _));
    }

    [Fact]
    public async Task Swagger_PostJob_HasKitchenAndBillExamples()
    {
        var client = factory.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var examples = doc.GetProperty("paths").GetProperty("/api/jobs").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("examples");
        Assert.Equal("kitchen", examples.GetProperty("kitchen").GetProperty("value").GetProperty("type").GetString());
        Assert.Equal("bill", examples.GetProperty("bill").GetProperty("value").GetProperty("type").GetString());
    }

    [Fact]
    public async Task PostJob_LinesWithExplicitTypeLines_Returns201()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs",
            Json($$"""{"deviceId":"{{Guid.NewGuid()}}","type":"lines","lines":[{{TextBody}}]}"""));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private const string OkData = """{"billNumber":"A","table":"1","cashier":"V","items":[{"name":"A","quantity":1}]}""";

    [Theory]
    [InlineData("""{"deviceId":"{D}","type":"kitchen","lines":[{"type":"text","text":"hi"}]}""")]
    [InlineData("""{"deviceId":"{D}","type":"kitchen"}""")]
    [InlineData("""{"deviceId":"{D}","type":"bill"}""")]
    [InlineData("""{"deviceId":"{D}","type":"lines","data":{"billNumber":"A","table":"1","cashier":"V","items":[{"name":"A","quantity":1}]}}""")]
    [InlineData("""{"deviceId":"{D}","data":{"billNumber":"A","table":"1","cashier":"V","items":[{"name":"A","quantity":1}]}}""")]
    [InlineData("""{"deviceId":"{D}","type":"receipt","data":{"billNumber":"A","table":"1","cashier":"V","items":[{"name":"A","quantity":1}]}}""")]
    [InlineData("""{"deviceId":"{D}","type":"kitchen","data":{"billNumber":"A","table":"1","cashier":"V","items":[]}}""")]
    [InlineData("""{"deviceId":"{D}","type":"kitchen","lines":[{"type":"text","text":"hi"}],"data":{"billNumber":"A","table":"1","cashier":"V","items":[{"name":"A","quantity":1}]}}""")]
    public async Task PostJob_WrongTypeDataCombination_Returns400(string body)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json(body.Replace("{D}", Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostJob_UnknownType_ErrorNamesType()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/jobs", Json($$"""{"deviceId":"{{Guid.NewGuid()}}","type":"receipt","data":{{OkData}}}"""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("type", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostJob_BillWithoutTotal_Returns400NamingTotal()
    {
        var client = factory.CreateClient();
        var body = BillBody.Replace("{DEVICE}", Guid.NewGuid().ToString()).Replace("\"total\": 353100,", "");
        var response = await client.PostAsync("/api/jobs", Json(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("total обязателен", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetJob_ReturnsTypeAndDataOrLines()
    {
        var client = factory.CreateClient();
        var device = Guid.NewGuid();
        var kitchen = await client.PostAsync("/api/jobs", Json(KitchenBody.Replace("{DEVICE}", device.ToString())));
        var kitchenId = (await kitchen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();
        var lines = await client.PostAsync("/api/jobs", Json(JobJson(device)));
        var linesId = (await lines.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetString();

        var k = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{kitchenId}");
        Assert.Equal("kitchen", k.GetProperty("type").GetString());
        Assert.Equal("A20", k.GetProperty("data").GetProperty("billNumber").GetString());
        Assert.Equal(JsonValueKind.Null, k.GetProperty("lines").ValueKind);

        var l = await client.GetFromJsonAsync<JsonElement>($"/api/jobs/{linesId}");
        Assert.Equal("lines", l.GetProperty("type").GetString());
        Assert.Equal("text", l.GetProperty("lines")[0].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, l.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task GetNext_RepeatedDispatch_ReturnsSameTypeAndData()
    {
        var client = ClientAt(new FakeTimeProvider(Now));
        var device = Guid.NewGuid();
        var first = await PostAndGetNextAsync(client, BillBody, device);
        var second = await client.GetStringAsync($"/api/devices/{device}/jobs/next");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PostJob_SixtyLongItems_Over8Kb_Returns413Not400()
    {
        using var kestrelFactory = new WebApplicationFactory<Program>();
        kestrelFactory.UseKestrel(0);
        var client = kestrelFactory.CreateClient();
        var item = $$"""{"name":"{{new string('a', 100)}}","quantity":1,"sum":999999999}""";
        var json = $$$"""{"deviceId":"{{{Guid.NewGuid()}}}","type":"bill","data":{"billNumber":"A","table":"1","cashier":"V","subtotal":1,"total":1,"items":[{{{string.Join(",", Enumerable.Repeat(item, 60))}}}]}}""";
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 8193, 9000);
        var response = await client.PostAsync("/api/jobs", Json(json));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
