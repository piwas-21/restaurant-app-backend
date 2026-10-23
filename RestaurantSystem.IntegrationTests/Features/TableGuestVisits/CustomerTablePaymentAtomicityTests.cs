using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Moq;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTablePaymentAtomicityTests(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    private const string Path = "/api/customer-mobile/table-payments/";
    private bool FailAfterReservation { get; set; }
    private bool ReservationObserved { get; set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<ICustomerTablePaymentAcceptance>();
        services.AddScoped<ICustomerTablePaymentAcceptance>(provider => new AcceptanceHook(
            ActivatorUtilities.CreateInstance<CustomerTablePaymentAcceptance>(provider), () =>
            {
                ReservationObserved = true;
                if (FailAfterReservation) throw new ConflictException("Fixture failure after actual reservation.");
            }));
    }

    [Fact]
    public async Task Failure_after_real_reservation_rolls_back_quote_hold_and_decision_before_original_retry()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var request = new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof };
        FailAfterReservation = true;
        (await SendAsync(Path + "submit", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        ReservationObserved.Should().BeTrue();
        await NoPaymentEffectsAsync();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(0);
        FailAfterReservation = false;
        (await PaymentResolutionAsync(await SendAsync(Path + "submit", request))).State.Should().Be(AccountPaymentState.Reserved);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Different_original_operations_cannot_both_claim_the_same_prepared_debt()
    {
        var intent = await PaymentIntentAsync();
        var other = intent with { OperationId = Guid.NewGuid() };
        var prepared = await PreparedPaymentAsync(intent); var otherPrepared = await PreparedPaymentAsync(other);
        var responses = await Task.WhenAll(
            SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }),
            SendAsync(Path + "submit", new CustomerTablePaymentSubmissionDto { Intent = other, Proof = otherPrepared.Proof }));
        responses.Count(response => response.IsSuccessStatusCode).Should().Be(1);
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(1);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Stop_and_late_submit_serialise_to_one_permanent_original_decision()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var responses = await Task.WhenAll(SendAsync(Path + "stop", prepared.Proof), SendAsync(Path + "submit",
            new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }));
        var first = await PaymentResolutionAsync(responses[0]);
        (await PaymentResolutionAsync(responses[1])).Should().Be(first);
        first.Outcome.Should().BeOneOf(CustomerTablePaymentOutcome.Stopped, CustomerTablePaymentOutcome.Applied);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(first.Outcome == CustomerTablePaymentOutcome.Applied ? 1 : 0);
    }

    [Fact]
    public async Task Stop_preserves_a_started_unknown_attempt_after_participant_expiry_and_feature_withdrawal()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var original = await PaymentResolutionAsync(await SendAsync(Path + "submit",
            new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }));
        await using var context = DatabaseFixture.CreateContext();
        await context.AccountPaymentAttempts.ExecuteUpdateAsync(update => update.SetProperty(row => row.State, AccountPaymentState.Starting)
            .SetProperty(row => row.StartedAt, DateTime.UtcNow));
        await context.TableGuestParticipants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        WithdrawNative(); Client.DefaultRequestHeaders.Remove("X-Table-Participant");
        var stopped = await PaymentResolutionAsync(await SendAsync(Path + "stop", prepared.Proof));
        stopped.Outcome.Should().Be(CustomerTablePaymentOutcome.Applied);
        stopped.State.Should().Be(AccountPaymentState.Starting); stopped.AttemptId.Should().Be(original.AttemptId);
        (await context.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Starting);
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(1);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Missing_retained_attempt_fails_closed_and_never_becomes_an_unseen_or_replacement_payment()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        var request = new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof };
        await PaymentResolutionAsync(await SendAsync(Path + "submit", request));
        await using var context = DatabaseFixture.CreateContext();
        await context.AccountPaymentAllocations.ExecuteDeleteAsync();
        await context.AccountPaymentAttempts.ExecuteDeleteAsync();
        (await SendAsync(Path + "recover", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(Path + "stop", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(Path + "submit", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Ordinary_web_checkout_cannot_start_a_native_reservation_even_with_its_private_operation()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        await PaymentResolutionAsync(await SendAsync(Path + "submit",
            new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }));
        await using var context = DatabaseFixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.SingleAsync();
        using var scope = Factory.Services.CreateScope();
        var journals = scope.ServiceProvider.GetRequiredService<IAccountCheckoutJournalStore>();
        var start = () => journals.FreezeGuestAsync(Visit.ServiceSessionId, attempt.OperationId, attempt.Version,
            Visit.ParticipantToken, RestaurantSystem.Api.Features.TableGuestVisits.Services.TableGuestCredentialCrypto.CreateParticipantToken(),
            CancellationToken.None);
        await start.Should().ThrowAsync<NotFoundException>();
        (await context.AccountCheckoutJournals.CountAsync()).Should().Be(0);
        Gateway.Invocations.Should().NotContain(call => call.Method.Name != "get_IsConfigured");
    }

    [Fact]
    public async Task Same_amount_with_different_retained_allocations_requires_reconciliation()
    {
        var intent = await PaymentIntentAsync(); var prepared = await PreparedPaymentAsync(intent);
        await PaymentResolutionAsync(await SendAsync(Path + "submit",
            new CustomerTablePaymentSubmissionDto { Intent = intent, Proof = prepared.Proof }));
        await using var context = DatabaseFixture.CreateContext();
        await context.AccountPaymentAllocations.ExecuteUpdateAsync(update => update.SetProperty(row => row.StartOrdinal, 2));
        (await context.AccountPaymentAttempts.SingleAsync()).AmountMinor.Should().Be(1400);
        (await context.AccountPaymentAllocations.SumAsync(row => row.AmountMinor)).Should().Be(1400);
        (await SendAsync(Path + "recover", prepared.Proof)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Set<CustomerTablePaymentOperation>().CountAsync()).Should().Be(1);
    }

    private sealed class AcceptanceHook(ICustomerTablePaymentAcceptance inner, Action after) : ICustomerTablePaymentAcceptance
    {
        public async Task<Guid> AcceptAsync(CustomerTablePaymentOwner owner, CustomerTablePaymentProofDto proof,
            CustomerTablePaymentBinding binding, CustomerTablePaymentIntentDto intent, string? participantToken,
            CancellationToken cancellationToken)
        {
            var result = await inner.AcceptAsync(owner, proof, binding, intent, participantToken, cancellationToken);
            after(); return result;
        }
    }
}
