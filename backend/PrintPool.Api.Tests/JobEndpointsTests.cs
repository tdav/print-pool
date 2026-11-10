using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

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
}
