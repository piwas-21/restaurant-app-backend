using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentRefundFinalizationEvidenceTests
{
    private static readonly DateTime ObservedAt = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Legacy_manual_till_evidence_accepts_empty_scopes_without_an_account_attempt()
    {
        using var context = CreateOfflineContext();
        var proof = CreateProof(Guid.NewGuid(), accountPaymentAttemptId: null, amountMinor: 1000, scopes: []);
        context.OrderAmendmentRefundEvidence.Add(proof.Evidence);

        var validate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, proof.Operation, [proof.Leg]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Account_bound_till_evidence_accepts_an_exact_unique_frozen_scope()
    {
        using var context = CreateOfflineContext();
        var scope = NewScope(amountMinor: 1000);
        var proof = CreateProof(Guid.NewGuid(), Guid.NewGuid(), 1000, [scope]);
        context.OrderAmendmentRefundEvidence.Add(proof.Evidence);

        var validate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, proof.Operation, [proof.Leg]);

        validate.Should().NotThrow();
    }

    [Fact]
    public void Account_bound_till_evidence_rejects_a_scope_amount_mismatch()
    {
        using var context = CreateOfflineContext();
        var proof = CreateProof(Guid.NewGuid(), Guid.NewGuid(), 1000, [NewScope(amountMinor: 999)]);
        context.OrderAmendmentRefundEvidence.Add(proof.Evidence);

        var validate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, proof.Operation, [proof.Leg]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Account_bound_till_evidence_rejects_duplicate_scope_identity_even_when_amount_matches()
    {
        using var context = CreateOfflineContext();
        var duplicate = NewScope(amountMinor: 500);
        var proof = CreateProof(Guid.NewGuid(), Guid.NewGuid(), 1000, [duplicate, duplicate]);
        context.OrderAmendmentRefundEvidence.Add(proof.Evidence);

        var validate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, proof.Operation, [proof.Leg]);

        validate.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Attempt_identity_and_frozen_scope_presence_must_remain_equivalent()
    {
        using var context = CreateOfflineContext();
        var legacyWithScopes = CreateProof(Guid.NewGuid(), accountPaymentAttemptId: null,
            amountMinor: 1000, scopes: [NewScope(amountMinor: 1000)]);
        context.OrderAmendmentRefundEvidence.Add(legacyWithScopes.Evidence);
        var emptyAttemptScope = CreateProof(Guid.NewGuid(), Guid.NewGuid(), amountMinor: 1000, scopes: []);
        context.OrderAmendmentRefundEvidence.Add(emptyAttemptScope.Evidence);

        var legacyValidate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, legacyWithScopes.Operation, [legacyWithScopes.Leg]);
        var accountValidate = () => OrderAmendmentRefundFinalizationEvidence.ValidateSucceededEvidence(
            context, emptyAttemptScope.Operation, [emptyAttemptScope.Leg]);

        legacyValidate.Should().Throw<ConflictException>();
        accountValidate.Should().Throw<ConflictException>();
    }

    private static ApplicationDbContext CreateOfflineContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=9;Database=offline_finalization_evidence")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static (OrderAmendmentResolutionOperation Operation, OrderAmendmentRefundLeg Leg,
        OrderAmendmentRefundEvidence Evidence) CreateProof(
        Guid sourcePaymentId, Guid? accountPaymentAttemptId, long amountMinor,
        IReadOnlyList<OrderAmendmentRefundScope> scopes)
    {
        var operationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        const string tillReference = "cash-2026/0042#1";
        var operation = new OrderAmendmentResolutionOperation
        {
            CreatedBy = nameof(OrderAmendmentRefundFinalizationEvidenceTests),
            Id = operationId,
            ActorUserId = actorId,
            ActorRole = nameof(UserRole.Admin)
        };
        var leg = new OrderAmendmentRefundLeg
        {
            CreatedBy = nameof(OrderAmendmentRefundFinalizationEvidenceTests),
            Id = legId,
            OperationId = operationId,
            SourcePaymentId = sourcePaymentId,
            AccountPaymentAttemptId = accountPaymentAttemptId,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = amountMinor,
            Currency = "CHF",
            FrozenScopesJson = OrderAmendmentJson.Serialize(scopes),
            ManualTillReference = tillReference,
            ResolvedAt = ObservedAt
        };
        var evidence = new OrderAmendmentRefundEvidence
        {
            CreatedBy = nameof(OrderAmendmentRefundFinalizationEvidenceTests),
            Id = Guid.NewGuid(),
            RefundLegId = legId,
            Kind = OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = amountMinor,
            Currency = "CHF",
            ActorUserId = actorId,
            ActorRole = operation.ActorRole,
            ObservedAt = ObservedAt,
            TillReference = tillReference
        };
        return (operation, leg, evidence);
    }

    private static OrderAmendmentRefundScope NewScope(long amountMinor) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, amountMinor, amountMinor);
}
