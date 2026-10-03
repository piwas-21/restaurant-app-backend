using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

[Collection("Database Lane 4")]
public sealed class TableReadinessRecoveryHttpTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private Guid _tableId;
    private Guid _operationId;

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.TableVisitReadinessV1 = false);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_lookup_with_flag_off_reads_frozen_outcome_without_changing_the_current_table(bool succeeded)
    {
        await SeedReceiptAsync(succeeded);
        AuthenticateAsRole(UserRole.Server);
        var result = await GetFromJsonAsync<ApiResponse<TableReadinessOperationDto>>(Path());
        result.Should().NotBeNull();
        result!.Success.Should().Be(succeeded);
        if (succeeded)
        {
            result.Data!.ReadinessState.Should().Be("ReadyForGuests");
            result.Data.ReadinessVersion.Should().Be(2);
            result.Data.OperationId.Should().Be(_operationId);
        }
        else result.ErrorCode.Should().Be(ErrorCodes.TableReadinessVisitOpen);
        await using var verify = DatabaseFixture.CreateContext();
        var current = await verify.Tables.SingleAsync(value => value.Id == _tableId);
        current.ReadinessVersion.Should().Be(5);
        current.ReadinessState.Should().Be(TableReadinessState.NeedsReset);
        (await verify.TableReadyOperations.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    public async Task Other_actor_or_role_cannot_read_the_servers_receipt(UserRole role)
    {
        await SeedReceiptAsync(succeeded: true);
        if (role == UserRole.Admin) AuthenticateAsAdmin();
        else AuthenticateAsRole(role);
        var result = await GetFromJsonAsync<ApiResponse<TableReadinessOperationDto>>(Path());
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableReadinessOperationNotFound);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Missing_or_wrong_table_operation_returns_the_same_private_unavailable_result()
    {
        await SeedReceiptAsync(succeeded: true);
        AuthenticateAsRole(UserRole.Server);
        var missing = await GetFromJsonAsync<ApiResponse<TableReadinessOperationDto>>(
            $"/api/Tables/{_tableId}/ready/operations/{Guid.NewGuid()}");
        var wrongTable = await GetFromJsonAsync<ApiResponse<TableReadinessOperationDto>>(
            $"/api/Tables/{Guid.NewGuid()}/ready/operations/{_operationId}");
        missing!.ErrorCode.Should().Be(ErrorCodes.TableReadinessOperationNotFound);
        wrongTable!.ErrorCode.Should().Be(missing.ErrorCode);
        wrongTable.Message.Should().Be(missing.Message);
    }

    [Fact]
    public async Task Anonymous_caller_cannot_read_a_readiness_receipt()
    {
        await SeedReceiptAsync(succeeded: true);
        AuthenticateAsAnonymous();
        using var response = await Client.GetAsync(Path());
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private string Path() => $"/api/Tables/{_tableId}/ready/operations/{_operationId}";

    private async Task SeedReceiptAsync(bool succeeded)
    {
        _tableId = Guid.NewGuid();
        _operationId = Guid.NewGuid();
        await using var context = DatabaseFixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "LOOKUP-QA",
            MaxGuests = 4,
            ReadinessVersion = 5,
            ReadinessState = TableReadinessState.NeedsReset,
            CreatedBy = nameof(TableReadinessRecoveryHttpTests)
        });
        context.TableReadyOperations.Add(new TableReadyOperation
        {
            TableId = _tableId,
            OperationId = _operationId,
            ActorUserId = Guid.Parse(TestAuthHandler.StaffUserId),
            ActorRole = UserRole.Server,
            ExpectedReadinessVersion = 1,
            Succeeded = succeeded,
            OutcomeState = succeeded ? TableReadinessState.ReadyForGuests : TableReadinessState.NeedsReset,
            OutcomeReadinessVersion = succeeded ? 2 : 1,
            OutcomeErrorCode = succeeded ? null : ErrorCodes.TableReadinessVisitOpen,
            RecordedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableReadinessRecoveryHttpTests)
        });
        await context.SaveChangesAsync();
    }
}
