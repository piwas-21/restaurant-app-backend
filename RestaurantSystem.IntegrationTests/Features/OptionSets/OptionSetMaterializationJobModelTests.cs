using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

public sealed class OptionSetMaterializationJobModelTests
{
    [Fact]
    public void Job_model_maps_immutable_json_and_idempotency_constraints()
    {
        using var connection = new NpgsqlConnection();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection)
            .Options;
        using var context = new ApplicationDbContext(options);
        var job = context.Model.FindEntityType(typeof(OptionSetMaterializationJob));
        var target = context.Model.FindEntityType(typeof(OptionSetMaterializationJobTarget));

        job.Should().NotBeNull();
        target.Should().NotBeNull();
        job!.GetTableName().Should().Be("OptionSetMaterializationJobs");
        target!.GetTableName().Should().Be("OptionSetMaterializationJobTargets");
        job.FindProperty(nameof(OptionSetMaterializationJob.RequestJson))!.GetColumnType().Should().Be("jsonb");
        target.FindProperty(nameof(OptionSetMaterializationJobTarget.RequestJson))!.GetColumnType().Should().Be("jsonb");
        target.FindProperty(nameof(OptionSetMaterializationJobTarget.ResultJson))!.GetColumnType().Should().Be("jsonb");
        job.GetIndexes().Should().Contain(index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(OptionSetMaterializationJob.OptionSetId), nameof(OptionSetMaterializationJob.IdempotencyKey) }));
        target.GetIndexes().Should().Contain(index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(OptionSetMaterializationJobTarget.JobId), nameof(OptionSetMaterializationJobTarget.Sequence) }));
    }
}
