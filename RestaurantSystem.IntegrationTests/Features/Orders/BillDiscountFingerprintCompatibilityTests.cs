using System.Text.Json;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class BillDiscountFingerprintCompatibilityTests
{
    [Fact]
    public void Existing_in_flight_resolution_keeps_the_deployed_source_fingerprint()
    {
        var state = State();
        // Independently derived by executing the pre-discount deployed fingerprint implementation.
        Assert.Equal("764a7ad0a582d09efb214d1fdbf4252da1115f10fc5d6d946eb95db4f864a0d2", // pragma: allowlist secret -- fixed compatibility hash
            OrderAmendmentFinancialSourceFingerprint.Create(state));
    }

    [Fact]
    public void New_adjustment_allocation_evidence_changes_the_fingerprint_even_at_the_same_total()
    {
        var state = State();
        state.Source.BillChargeAmount = -4m;
        var charge = new OrderBillCharge(Identity(4), "discount", "Discount", -4m, "EUR", DateTime.UnixEpoch,
            "test", 4m, "Reduce", "Fixed", 2000, [new(Identity(5), 1, 2, 200)]);
        state.Source.BillChargesJson = JsonSerializer.Serialize(new[] { charge }, BillChargeRules.JsonOptions);
        var original = OrderAmendmentFinancialSourceFingerprint.Create(state);
        state.Source.BillChargesJson = JsonSerializer.Serialize(new[] { charge with {
            Allocations = [new(Identity(5), 2, 2, 200)] } }, BillChargeRules.JsonOptions);
        Assert.NotEqual(original, OrderAmendmentFinancialSourceFingerprint.Create(state));
    }

    [Fact]
    public void Legacy_staff_charge_request_keeps_its_original_serialized_fingerprint_payload()
    {
        var input = new RestaurantSystem.Api.Features.BillCharges.Dtos.BillChargeInputDto("service_fee", 5m);
        Assert.Equal("{\"Key\":\"service_fee\",\"Amount\":5}", JsonSerializer.Serialize(input));
    }

    private static OrderAmendmentFinancialSourceState State()
    {
        var source = new Order { Id = Identity(1), Type = OrderType.Takeaway, Total = 20m, CreatedBy = "test" };
        var amendment = new OrderAmendment
        {
            Id = Identity(2),
            SourceOrderId = source.Id,
            ActorUserId = Identity(3),
            ActorRole = "Admin",
            State = OrderAmendmentState.Committed,
            ChangesJson = "[]",
            FinancialResolutionJson = "{}",
            SourceSnapshotJson = "{}",
            CreatedBy = "test"
        };
        return new(source, amendment, [], [], [], [], new Dictionary<Guid, long>(), [], [], "EUR");
    }
    private static Guid Identity(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}
