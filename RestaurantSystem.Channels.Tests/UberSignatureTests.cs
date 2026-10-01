using System.Text;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class UberSignatureTests
{
    [Fact]
    public void HmacMatchesIndependentRFC4231TestCase2AndRejectsModifiedBytes()
    {
        // Independent oracle: RFC 4231 section 4.3, not a second call to the signing implementation.
        const string signature = "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"; // pragma: allowlist secret -- public RFC 4231 test vector
        Assert.True(UberSignature.Verify(Encoding.ASCII.GetBytes("what do ya want for nothing?"), signature, "Jefe"));
        Assert.False(UberSignature.Verify(Encoding.ASCII.GetBytes("what do ya want for nothing!"), signature, "Jefe"));
        Assert.False(UberSignature.Verify(Encoding.ASCII.GetBytes("what do ya want for nothing?"), signature, "wrong"));
        Assert.False(UberSignature.Verify([], "not-hex", "Jefe"));
    }

    [Theory]
    [InlineData("wrong-client")]
    [InlineData("")]
    public void ProvisioningForAnotherApplicationCannotEnterTheInbox(string declaredClient)
    {
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            event_type = "store.provisioned",
            store_id = GatewayFixture.StoreId,
            webhook_meta = new { client_id = declaredClient, webhook_msg_uuid = "event-1", webhook_msg_timestamp = 1623317195 },
        });
        Assert.Null(UberNotification.Parse(body, GatewayFixture.ClientId, DateTimeOffset.UtcNow));
    }
}
