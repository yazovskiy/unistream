using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using T3.Api.Models;

namespace T3.Api.Tests;

public sealed class TransactionApiTests
{
    [Fact]
    // Проверяет базовый сценарий: POST сохраняет транзакцию, GET возвращает её по Id.
    public async Task Post_Then_Get_Returns_Transaction()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var request = new Transaction(Guid.NewGuid(), DateTime.UtcNow.Date, 12.34m);

        var postResponse = await client.PostAsJsonAsync("/api/v1/Transaction", request);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/Transaction?id={request.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var body = await getResponse.Content.ReadFromJsonAsync<Transaction>();
        Assert.NotNull(body);
        Assert.Equal(request.Id, body!.Id);
        Assert.Equal(request.Amount, body.Amount);
    }

    [Fact]
    // Проверяет идемпотентность: повторный POST возвращает тот же insertDateTime.
    public async Task Post_Is_Idempotent_Returns_Same_InsertDateTime()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var request = new Transaction(Guid.NewGuid(), DateTime.UtcNow.Date, 5m);

        var first = await client.PostAsJsonAsync("/api/v1/Transaction", request);
        var second = await client.PostAsJsonAsync("/api/v1/Transaction", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            firstBody.GetProperty("insertDateTime").GetString(),
            secondBody.GetProperty("insertDateTime").GetString()
        );
    }

    [Fact]
    // В строгом режиме при разных данных с тем же Id возвращается 409 Conflict.
    public async Task Strict_Idempotency_Different_Payload_Returns_Conflict()
    {
        await using var factory = new ApiFactory(strictIdempotency: true);
        using var client = factory.CreateClient();

        var id = Guid.NewGuid();
        var request1 = new Transaction(id, DateTime.UtcNow.Date, 10m);
        var request2 = new Transaction(id, DateTime.UtcNow.Date, 11m);

        var first = await client.PostAsJsonAsync("/api/v1/Transaction", request1);
        var second = await client.PostAsJsonAsync("/api/v1/Transaction", request2);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    // Негативный amount должен приводить к ошибке валидации (400).
    public async Task Validation_Fails_For_Negative_Amount()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var request = new Transaction(Guid.NewGuid(), DateTime.UtcNow.Date, -1m);

        var response = await client.PostAsJsonAsync("/api/v1/Transaction", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    // После 100 записей следующая должна вернуть 409 Conflict (лимит ёмкости).
    public async Task Capacity_Limit_Returns_Conflict()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 100; i++)
        {
            var request = new Transaction(Guid.NewGuid(), DateTime.UtcNow.Date, 1m + i);
            var response = await client.PostAsJsonAsync("/api/v1/Transaction", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var overflow = new Transaction(Guid.NewGuid(), DateTime.UtcNow.Date, 999m);
        var overflowResponse = await client.PostAsJsonAsync("/api/v1/Transaction", overflow);
        Assert.Equal(HttpStatusCode.Conflict, overflowResponse.StatusCode);
    }
}
