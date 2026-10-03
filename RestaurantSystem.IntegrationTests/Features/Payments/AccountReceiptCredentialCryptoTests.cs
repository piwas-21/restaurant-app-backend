using FluentAssertions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountReceiptCredentialCryptoTests
{
    [Fact]
    public void Visit_credential_hash_cannot_be_used_as_a_receipt_grant()
    {
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        TableGuestCredentialCrypto.TryHashParticipantToken(token, out var participantHash).Should().BeTrue();
        AccountReceiptCredentialCrypto.TryHash(token, out var receiptHash).Should().BeTrue();
        receiptHash.Should().NotBe(participantHash);
        AccountReceiptCredentialCrypto.Verify(token, receiptHash).Should().BeTrue();
        AccountReceiptCredentialCrypto.Verify(token, participantHash).Should().BeFalse();
        AccountReceiptCredentialCrypto.Verify(TableGuestCredentialCrypto.CreateParticipantToken(), receiptHash)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    public void Noncanonical_or_missing_secret_has_no_receipt_authority(string? token)
    {
        AccountReceiptCredentialCrypto.TryHash(token, out var hash).Should().BeFalse();
        hash.Should().BeEmpty();
        AccountReceiptCredentialCrypto.Verify(token, new string('0', 64)).Should().BeFalse();
    }

    [Fact]
    public void Malformed_persisted_hash_fails_closed()
    {
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        AccountReceiptCredentialCrypto.Verify(token, null).Should().BeFalse();
        AccountReceiptCredentialCrypto.Verify(token, new string('G', 64)).Should().BeFalse();
        AccountReceiptCredentialCrypto.Verify(token, "short").Should().BeFalse();
    }
}
