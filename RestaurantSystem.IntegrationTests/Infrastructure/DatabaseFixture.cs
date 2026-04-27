using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using RestaurantSystem.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private Respawner _respawner = null!;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Create and start PostgreSQL container
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("restaurant_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        ConnectionString = _postgres.GetConnectionString();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson(); // 👈 this line is required
        var dataSource = dataSourceBuilder.Build();



        // Run migrations
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Setup Respawner for database cleanup between tests
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = new[] { "public" },
            TablesToIgnore = new Table[] { "__EFMigrationsHistory" }
        });
    }

    public ApplicationDbContext CreateContext()
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        return new ApplicationDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        if (_postgres != null)
        {
            await _postgres.DisposeAsync();
        }
    }
}
