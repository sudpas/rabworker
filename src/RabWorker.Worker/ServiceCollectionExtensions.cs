using Microsoft.Extensions.Options;
using Npgsql;

namespace RabWorker.Worker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFormWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.Section));
        services.AddSingleton(sp =>
            NpgsqlDataSource.Create(sp.GetRequiredService<IOptions<WorkerOptions>>().Value.PostgresConnectionString));
        services.AddSingleton<FormRepository>();
        services.AddSingleton<FormUpdateHandler>();
        // Hosted services start in registration order; schema must exist before consuming.
        services.AddHostedService<SchemaInitializer>();
        services.AddHostedService<FormUpdateConsumer>();
        return services;
    }
}
