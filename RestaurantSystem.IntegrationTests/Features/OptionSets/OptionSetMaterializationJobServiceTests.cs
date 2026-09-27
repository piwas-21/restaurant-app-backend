using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

[Collection("Database Lane 1")]
public sealed class OptionSetMaterializationJobServiceTests : IntegrationTestBase
{
    private const string Actor = "option-set-materialization-job-service-test";

    public OptionSetMaterializationJobServiceTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Create_replays_large_job_across_service_restart_and_rejects_key_reuse()
    {
        var optionSet = new OptionSet
        {
            Id = Guid.NewGuid(),
            Kind = OptionSetKind.Ingredient,
            Name = $"Test set {Guid.NewGuid():N}",
            NormalizedName = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        await using (var setup = DatabaseFixture.CreateContext())
        {
            setup.OptionSets.Add(optionSet);
            await setup.SaveChangesAsync();
        }

        var materializer = new Mock<IOptionSetMaterializer>(MockBehavior.Strict);
        materializer.Setup(service => service.ValidateJobRequestAsync(
                It.IsAny<OptionSetMaterializationRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var request = CreateRequest(optionSet.Id, "large-idempotent-job");
        OptionSetMaterializationJobDto created;
        await using (var firstProcessContext = DatabaseFixture.CreateContext())
        {
            created = await CreateService(firstProcessContext, materializer.Object)
                .CreateAsync(optionSet.Id, request, CancellationToken.None);
        }

        OptionSetMaterializationJobDto replayed;
        await using (var restartedProcessContext = DatabaseFixture.CreateContext())
        {
            var service = CreateService(restartedProcessContext, materializer.Object);
            replayed = await service
                .CreateAsync(optionSet.Id, request, CancellationToken.None);
            (await service.GetAsync(optionSet.Id, created.JobId, CancellationToken.None))
                .JobId.Should().Be(created.JobId);
            restartedProcessContext.ChangeTracker.Entries().Should().BeEmpty();
        }

        replayed.JobId.Should().Be(created.JobId);
        replayed.Targets.Should().HaveCount(101)
            .And.OnlyContain(target => target.Status == "pending" && target.Attempts == 0);

        var conflictingRequest = new OptionSetMaterializationRequest
        {
            OptionSetId = request.OptionSetId,
            ExpectedSetVersion = request.ExpectedSetVersion,
            IdempotencyKey = request.IdempotencyKey,
            Targets = request.Targets.Select((target, index) => new OptionSetMaterializationTargetRequest
            {
                TargetKey = index == 0 ? "changed-target" : target.TargetKey,
                Role = target.Role,
                TargetProductId = target.TargetProductId
            }).ToArray()
        };
        await using (var retryContext = DatabaseFixture.CreateContext())
        {
            var act = () => CreateService(retryContext, materializer.Object)
                .CreateAsync(optionSet.Id, conflictingRequest, CancellationToken.None);
            await act.Should().ThrowAsync<ConflictException>();
        }

        materializer.Verify(service => service.ValidateJobRequestAsync(
            It.IsAny<OptionSetMaterializationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OptionSetMaterializationJobs.CountAsync()).Should().Be(1);
        (await verify.OptionSetMaterializationJobTargets.CountAsync()).Should().Be(101);
    }

    private static OptionSetMaterializationRequest CreateRequest(Guid optionSetId, string idempotencyKey) => new()
    {
        OptionSetId = optionSetId,
        ExpectedSetVersion = 1,
        IdempotencyKey = idempotencyKey,
        Targets = Enumerable.Range(0, 101).Select(index => new OptionSetMaterializationTargetRequest
        {
            TargetKey = $"target-{index:D3}",
            Role = OptionSetAttachmentRole.Ingredient,
            TargetProductId = Guid.NewGuid()
        }).ToArray()
    };

    private static OptionSetMaterializationJobService CreateService(
        ApplicationDbContext context,
        IOptionSetMaterializer materializer) => new(
        context,
        materializer,
        new TenantFeatures(Options.Create(new TenantFeatureSettings { OptionSetMaterializationEnabled = true })),
        Mock.Of<RestaurantSystem.Api.Common.Services.Interfaces.ICurrentUserService>(),
        Options.Create(new OptionSetAuthoringSettings
        {
            MaximumIdempotencyKeyLength = 100,
            MaximumTargetsPerRequest = 100,
            MaximumTargetsPerJob = 1000
        }));
}
