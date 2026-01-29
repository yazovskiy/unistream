using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using T3.Api.Data;

namespace T3.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _strictIdempotency;

    public ApiFactory(bool strictIdempotency = false)
    {
        _strictIdempotency = strictIdempotency;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // В тестах заменяем хранилище на in-memory реализацию.
            services.RemoveAll<ITransactionStore>();
            services.AddSingleton<ITransactionStore>(_ => new InMemoryTransactionStore(_strictIdempotency));
        });
    }
}
