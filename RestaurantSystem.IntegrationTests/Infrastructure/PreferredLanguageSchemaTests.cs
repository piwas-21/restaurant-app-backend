using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// The <c>preferred_language</c> columns of EMAIL-LOCALISATION-PLAN S2 — the first place a guest's
/// own language is durable rather than an accident of the request that happens to send the mail.
/// </summary>
/// <remarks>
/// Asserted against the real database, not the model: the three columns land on a table RUMI has
/// live data in, and the only property that matters to that data is that the migration is purely
/// additive — nullable, no default, no rewrite. A model-level assertion would hold even if the
/// migration had been written NOT NULL.
/// </remarks>
public class PreferredLanguageSchemaTests : IntegrationTestBase
{
    public PreferredLanguageSchemaTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Table names as Postgres holds them: the identity table keeps Identity's PascalCase (quoted),
    /// <c>orders</c> went through the snake_case pass. Getting this wrong is the trap called out in
    /// the workspace CLAUDE.md, and a query against a non-existent table would error rather than
    /// silently pass — hence the explicit row-count assertion below.
    /// </summary>
    public static TheoryData<string> TablesWithTheColumn => new() { "Users", "Reservations", "orders" };

    [Theory]
    [MemberData(nameof(TablesWithTheColumn))]
    public async Task The_column_is_nullable_with_no_default(string table)
    {
        var column = await ColumnAsync(table);

        column.Should().NotBeNull($"the migration must have added preferred_language to {table}");
        column!.IsNullable.Should().Be("YES",
            "an existing row has no language and inventing one would mail a guest in a language they never chose");
        column.ColumnDefault.Should().BeNull("a default would be exactly that invented language");
        column.MaxLength.Should().Be(LanguageCode.MaxLength);
        column.DataType.Should().Be("character varying");
    }

    /// <summary>
    /// The write path end to end: EF maps the property, Postgres stores it, and reading it back
    /// yields the canonical code. Null must survive too — it is the "no preference" signal the
    /// resolution chain depends on, and a column that silently coerced it to "" would make every
    /// rank-2 lookup answer with a language nobody chose.
    /// </summary>
    [Fact]
    public async Task A_stored_preference_round_trips_and_null_stays_null()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await context.Users.FirstAsync();
        user.PreferredLanguage.Should().BeNull("nothing captured a language before this slice");

        user.PreferredLanguage = LanguageCode.Normalize("FR-ch");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        (await context.Users.FirstAsync(u => u.Id == user.Id)).PreferredLanguage.Should().Be("fr");
    }

    /// <summary>
    /// A migration that cannot be undone is a one-way door on a live tenant. Down is asserted from
    /// the migration source rather than executed: rolling back on the shared test database would
    /// take the columns away from every other test in the collection.
    /// </summary>
    [Fact]
    public void The_migration_gives_all_three_columns_back()
    {
        var migration = File.ReadAllText(Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "RestaurantSystem.Infrastructure", "Persistence", "Migrations"),
                "*_AddPreferredLanguage.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal)));

        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        foreach (var table in new[] { "Users", "Reservations", "orders" })
        {
            down.Should().Contain($"table: \"{table}\"", $"Down must drop the column from {table}");
        }

        down.Split("DropColumn").Length.Should().Be(4, "one DropColumn per added column");
    }

    private async Task<ColumnShape?> ColumnAsync(string table)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT is_nullable, column_default, character_maximum_length, data_type
            FROM information_schema.columns
            WHERE table_name = @table AND column_name = 'preferred_language'
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ColumnShape(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.GetString(3));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed record ColumnShape(string IsNullable, string? ColumnDefault, int? MaxLength, string DataType);
}
