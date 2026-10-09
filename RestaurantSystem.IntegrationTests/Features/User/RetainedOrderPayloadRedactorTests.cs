using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;

namespace RestaurantSystem.IntegrationTests.Features.User;

public sealed class RetainedOrderPayloadRedactorTests
{
    [Fact]
    public void Redaction_clears_nested_operational_text_without_changing_financial_or_replay_evidence()
    {
        const string payload = """
            {
              "reason":"Customer private reason",
              "providerConsentNote":"Customer private provider note",
              "clientOperationId":"11111111-1111-4111-8111-111111111111",
              "pricingFingerprint":"unchanged-original-proof",
              "total":12.35,
              "changes":[{"kind":"Replace","startOrdinal":2,"quantity":1,
                "previous":{"id":"old-item","productName":"Original dish","unitPrice":12.35,
                  "specialInstructions":"Private instruction","sideItems":[
                    {"id":"child-item","SpecialInstructions":"Private nested instruction","quantity":2}
                  ]},
                "current":{"id":"new-item","productName":"New dish","quantity":1,"itemTotal":12.35}
              }]
            }
            """;

        using var result = JsonDocument.Parse(RetainedOrderPayloadRedactor.Redact(payload)!);
        var root = result.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("providerConsentNote").ValueKind);
        Assert.Equal("11111111-1111-4111-8111-111111111111", root.GetProperty("clientOperationId").GetString());
        Assert.Equal("unchanged-original-proof", root.GetProperty("pricingFingerprint").GetString());
        Assert.Equal(12.35m, root.GetProperty("total").GetDecimal());
        var change = root.GetProperty("changes")[0];
        Assert.Equal("Replace", change.GetProperty("kind").GetString());
        Assert.Equal(2, change.GetProperty("startOrdinal").GetInt32());
        Assert.Equal(1, change.GetProperty("quantity").GetInt32());
        var previous = change.GetProperty("previous");
        Assert.Equal("old-item", previous.GetProperty("id").GetString());
        Assert.Equal("Original dish", previous.GetProperty("productName").GetString());
        Assert.Equal(12.35m, previous.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, previous.GetProperty("specialInstructions").ValueKind);
        var child = previous.GetProperty("sideItems")[0];
        Assert.Equal("child-item", child.GetProperty("id").GetString());
        Assert.Equal(2, child.GetProperty("quantity").GetInt32());
        Assert.Equal(JsonValueKind.Null, child.GetProperty("SpecialInstructions").ValueKind);
        Assert.Equal("new-item", change.GetProperty("current").GetProperty("id").GetString());
        Assert.Equal(12.35m, change.GetProperty("current").GetProperty("itemTotal").GetDecimal());
    }

    [Fact]
    public void Redaction_leaves_absent_free_text_and_optional_null_payloads_unchanged()
    {
        Assert.Null(RetainedOrderPayloadRedactor.Redact(null));
        Assert.Equal("[]", RetainedOrderPayloadRedactor.Redact("[]"));
        Assert.Equal("{\"quantity\":3,\"amountMinor\":1235}",
            RetainedOrderPayloadRedactor.Redact("{\"quantity\":3,\"amountMinor\":1235}"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("12")]
    public void Invalid_retained_evidence_blocks_erasure_instead_of_losing_financial_structure(string payload)
    {
        Assert.Throws<ConflictException>(() => RetainedOrderPayloadRedactor.Redact(payload));
    }
}
