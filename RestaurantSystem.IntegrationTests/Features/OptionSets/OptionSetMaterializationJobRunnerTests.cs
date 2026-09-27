using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

[Collection("Database Lane 1")]
public sealed class OptionSetMaterializationJobRunnerTests : IntegrationTestBase
{
    private const string Actor = "option-set-materialization-job-test";
    private static readonly JsonSerializerOptions JobJsonOptions = CreateJobJsonOptions();

    public OptionSetMaterializationJobRunnerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Recovered_job_preflights_100_pending_targets_once_across_25_target_batches()
    {
        var targets = Enumerable.Range(0, 100).Select(index => Target(
            $"target-{index:D3}", OptionSetAttachmentRole.Ingredient, Guid.NewGuid())).ToArray();
        var job = await SaveJobAsync(targets, "processing", DateTime.UtcNow.AddSeconds(-1));
        var validatedTargetCounts = new List<int>();
        var applyCount = 0;

        var batchCount = 0;
        while (true)
        {
            await using var context = DatabaseFixture.CreateContext();
            var materializer = CreateMaterializer(context, validatedTargetCounts, _ => applyCount++);
            var runner = CreateRunner(context, materializer.Object);
            if (!await runner.RunNextBatchAsync(CancellationToken.None))
            {
                break;
            }

            batchCount++;
        }

        batchCount.Should().Be(4);
        applyCount.Should().Be(100);
        validatedTargetCounts.Should().Equal(100);
        await using var verify = DatabaseFixture.CreateContext();
        var savedJob = await verify.OptionSetMaterializationJobs
            .Include(item => item.Targets)
            .SingleAsync(item => item.Id == job.Id);
        savedJob.Status.Should().Be("completed");
        savedJob.Targets.Should().OnlyContain(target => target.Status == "applied" && target.Attempts == 1);
    }

    [Fact]
    public async Task Resume_preflights_pending_and_failed_targets_but_skips_completed_targets()
    {
        var targets = new[]
        {
            Target("completed", OptionSetAttachmentRole.Ingredient, Guid.NewGuid()),
            Target("pending", OptionSetAttachmentRole.Ingredient, Guid.NewGuid()),
            Target("failed", OptionSetAttachmentRole.Ingredient, Guid.NewGuid())
        };
        var job = await SaveJobAsync(targets, "partial", null, ["applied", "pending", "failed"]);
        var validatedRequests = new List<OptionSetMaterializationRequest>();
        var materializer = new Mock<IOptionSetMaterializer>(MockBehavior.Strict);
        materializer.Setup(service => service.ValidateJobRequestAsync(
                It.IsAny<OptionSetMaterializationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OptionSetMaterializationRequest, CancellationToken>((request, _) => validatedRequests.Add(request))
            .Returns(Task.CompletedTask);
        var settings = Options.Create(new OptionSetAuthoringSettings
        {
            MaximumIdempotencyKeyLength = 100,
            MaximumTargetsPerJob = 1000
        });
        await using var resumeContext = DatabaseFixture.CreateContext();
        var service = new OptionSetMaterializationJobService(
            resumeContext,
            materializer.Object,
            new TenantFeatures(Options.Create(new TenantFeatureSettings { OptionSetMaterializationEnabled = true })),
            Mock.Of<RestaurantSystem.Api.Common.Services.Interfaces.ICurrentUserService>(),
            settings);

        var resumed = await service.ResumeAsync(job.OptionSetId, job.Id, CancellationToken.None);

        resumed.Status.Should().Be("queued");
        validatedRequests.Should().ContainSingle();
        validatedRequests[0].Targets.Select(target => target.TargetKey)
            .Should().Equal("pending", "failed");
        await using var verify = DatabaseFixture.CreateContext();
        var saved = await verify.OptionSetMaterializationJobTargets
            .Where(target => target.JobId == job.Id)
            .OrderBy(target => target.Sequence)
            .Select(target => target.Status)
            .ToListAsync();
        saved.Should().Equal("applied", "pending", "pending");
    }

    [Fact]
    public async Task Previous_menu_version_skips_an_intervening_product_choice_result()
    {
        var menuProductId = Guid.NewGuid();
        var targets = new[]
        {
            Target("bundle-a", OptionSetAttachmentRole.BundleChoice, menuProductId,
                menuSectionId: Guid.NewGuid(), menuVersion: 1),
            Target("product-choice", OptionSetAttachmentRole.ProductChoice, menuProductId,
                customizationGroupId: Guid.NewGuid(), groupVersion: 1),
            Target("bundle-b", OptionSetAttachmentRole.BundleChoice, menuProductId,
                menuSectionId: Guid.NewGuid(), menuVersion: 1)
        };
        var job = await SaveJobAsync(targets, "queued", null);
        var previousMenuVersions = new List<int?>();
        await using var context = DatabaseFixture.CreateContext();
        var materializer = CreateMaterializer(context, [], previousMenuVersions.Add);
        var runner = CreateRunner(context, materializer.Object);

        (await runner.RunNextBatchAsync(CancellationToken.None)).Should().BeTrue();
        previousMenuVersions.Should().Equal(null, null, 2);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OptionSetMaterializationJobs.Where(item => item.Id == job.Id)
            .Select(item => item.Status).SingleAsync()).Should().Be("completed");
    }

    private async Task<OptionSetMaterializationJob> SaveJobAsync(
        IReadOnlyList<OptionSetMaterializationTargetRequest> targetRequests,
        string status,
        DateTime? leaseExpiresAt,
        IReadOnlyList<string>? targetStatuses = null)
    {
        var now = DateTime.UtcNow;
        var set = new OptionSet
        {
            Id = Guid.NewGuid(),
            Kind = targetRequests.Any(target => target.Role is OptionSetAttachmentRole.BundleChoice or OptionSetAttachmentRole.ProductChoice)
                ? OptionSetKind.BundleChoice
                : OptionSetKind.Ingredient,
            Name = $"Test set {Guid.NewGuid():N}",
            NormalizedName = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            CreatedBy = Actor
        };
        var request = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"job-{Guid.NewGuid():N}",
            Targets = targetRequests
        };
        var job = new OptionSetMaterializationJob
        {
            Id = Guid.NewGuid(),
            OptionSetId = set.Id,
            SetVersion = set.Version,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = OptionSetMaterializationJobJson.Fingerprint(request),
            RequestJson = Serialize(request),
            Status = status,
            LeaseId = status == "processing" ? Guid.NewGuid() : null,
            LeaseExpiresAt = leaseExpiresAt,
            StartedAt = status == "processing" ? now.AddMinutes(-1) : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = Actor
        };
        job.Targets = targetRequests.Select((target, sequence) => new OptionSetMaterializationJobTarget
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            Sequence = sequence,
            TargetKey = target.TargetKey,
            TargetProductId = target.TargetProductId,
            RequestJson = Serialize(target),
            Status = targetStatuses?[sequence] ?? "pending",
            Attempts = targetStatuses?[sequence] == "applied" ? 1 : 0,
            ResultJson = targetStatuses?[sequence] == "applied"
                ? Serialize(new OptionSetMaterializationTargetResultDto
                {
                    TargetKey = target.TargetKey,
                    Status = "applied"
                })
                : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = Actor
        }).ToList();

        await using var context = DatabaseFixture.CreateContext();
        context.OptionSets.Add(set);
        context.OptionSetMaterializationJobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }

    private static Mock<IOptionSetMaterializer> CreateMaterializer(
        ApplicationDbContext context,
        List<int> validatedTargetCounts,
        Action<int?> previousMenuVersionObserved)
    {
        var materializer = new Mock<IOptionSetMaterializer>(MockBehavior.Strict);
        materializer.Setup(service => service.ValidateJobRequestAsync(
                It.IsAny<OptionSetMaterializationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OptionSetMaterializationRequest, CancellationToken>((request, _) =>
                validatedTargetCounts.Add(request.Targets.Count))
            .Returns(Task.CompletedTask);
        materializer.Setup(service => service.ApplyJobTargetAsync(
                It.IsAny<OptionSetMaterializationRequest>(),
                It.IsAny<OptionSetMaterializationTargetRequest>(),
                It.IsAny<OptionSetMaterializationJobTarget>(),
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                OptionSetMaterializationRequest _,
                OptionSetMaterializationTargetRequest target,
                OptionSetMaterializationJobTarget jobTarget,
                Guid _,
                int? previousMenuVersion,
                string _,
                CancellationToken cancellationToken) =>
            {
                previousMenuVersionObserved(previousMenuVersion);
                return SaveAppliedResultAsync(context, target, jobTarget, previousMenuVersion, cancellationToken);
            });
        return materializer;
    }

    private static async Task<OptionSetMaterializationTargetResultDto> SaveAppliedResultAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest request,
        OptionSetMaterializationJobTarget jobTarget,
        int? previousMenuVersion,
        CancellationToken cancellationToken)
    {
        context.Attach(jobTarget);
        int? menuVersion = request.Role == OptionSetAttachmentRole.BundleChoice
            ? (previousMenuVersion ?? request.ExpectedMenuAuthoringVersion ?? 0) + 1
            : null;
        var result = new OptionSetMaterializationTargetResultDto
        {
            TargetKey = request.TargetKey,
            Status = "applied",
            MenuAuthoringVersion = menuVersion
        };
        jobTarget.Status = "applied";
        jobTarget.Attempts++;
        jobTarget.ResultJson = Serialize(result);
        jobTarget.CompletedAt = DateTime.UtcNow;
        jobTarget.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static OptionSetMaterializationJobRunner CreateRunner(
        ApplicationDbContext context,
        IOptionSetMaterializer materializer) => new(
            context,
            materializer,
            Options.Create(new OptionSetAuthoringSettings { TargetsPerJobRun = 25, JobLeaseSeconds = 300 }),
            NullLogger<OptionSetMaterializationJobRunner>.Instance);

    private static OptionSetMaterializationTargetRequest Target(
        string key,
        OptionSetAttachmentRole role,
        Guid productId,
        Guid? menuSectionId = null,
        int? menuVersion = null,
        Guid? customizationGroupId = null,
        int? groupVersion = null) => new()
        {
            TargetKey = key,
            Role = role,
            TargetProductId = productId,
            TargetMenuSectionId = menuSectionId,
            ExpectedMenuAuthoringVersion = menuVersion,
            TargetCustomizationGroupId = customizationGroupId,
            ExpectedCustomizationGroupVersion = groupVersion
        };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JobJsonOptions);

    private static JsonSerializerOptions CreateJobJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StringEnumConverterFactory());
        return options;
    }
}
