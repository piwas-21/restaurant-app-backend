using FluentAssertions;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public sealed class TableGuestCredentialCryptoTests
{
    [Fact]
    public void Admission_codes_are_random_normalized_and_verified_without_storing_plaintext()
    {
        var first = TableGuestCredentialCrypto.CreateAdmissionCode();
        var second = TableGuestCredentialCrypto.CreateAdmissionCode();
        var shortCode = TableGuestCredentialCrypto.CreateAdmissionCode(preferShortCode: true);

        first.Code.Should().HaveLength(TableGuestCredentialCrypto.AdmissionCodeLength);
        first.Code.Should().MatchRegex("^[0-9A-HJKMNP-TV-Z]{10}$");
        shortCode.Code.Should().HaveLength(TableGuestCredentialCrypto.CurrentAdmissionCodeLength);
        shortCode.Code.Should().MatchRegex("^[0-9A-HJKMNP-TV-Z]{6}$");
        first.Hash.Should().NotContain(first.Code);
        TableGuestCredentialCrypto.VerifyAdmissionCode(first.Code.ToLowerInvariant(), first.Hash).Should().BeTrue();
        TableGuestCredentialCrypto.VerifyAdmissionCode(shortCode.Code, shortCode.Hash).Should().BeTrue();
        TableGuestCredentialCrypto.VerifyAdmissionCode(second.Code, first.Hash).Should().BeFalse();
        TableGuestCredentialCrypto.HashAdmissionCode(first.Code).Should().NotBe(first.Hash);
        const string previouslyIssuedCode = "123456789A";
        var previouslyIssuedHash = TableGuestCredentialCrypto.HashAdmissionCode(previouslyIssuedCode);
        TableGuestCredentialCrypto.VerifyAdmissionCode(previouslyIssuedCode, previouslyIssuedHash).Should().BeTrue();
        TableGuestCredentialCrypto.VerifyAdmissionCode(
            previouslyIssuedCode.ToLowerInvariant(), previouslyIssuedHash).Should().BeTrue();
        TableGuestCredentialCrypto.TryNormalizeAdmissionCode("O123456789", out _).Should().BeFalse();
        TableGuestCredentialCrypto.TryNormalizeAdmissionCode("123456789", out _).Should().BeFalse();
        TableGuestCredentialCrypto.TryNormalizeAdmissionCode("12345", out _).Should().BeFalse();
    }

    [Fact]
    public void Participant_tokens_are_canonical_opaque_credentials_and_only_their_digest_is_stored()
    {
        var token = TableGuestCredentialCrypto.CreateParticipantToken();
        var valid = TableGuestCredentialCrypto.TryHashParticipantToken(token, out var digest);

        valid.Should().BeTrue();
        token.Should().HaveLength(43);
        digest.Should().HaveLength(64);
        digest.Should().NotContain(token);
        TableGuestCredentialCrypto.TryHashParticipantToken(token + "=", out _).Should().BeFalse();
        TableGuestCredentialCrypto.TryHashParticipantToken(token[..^1] + "B", out _).Should().BeFalse();
        TableGuestCredentialCrypto.TryHashParticipantToken("not-a-token", out _).Should().BeFalse();
    }

    [Fact]
    public void Round_request_hash_binds_the_visit_participant_operation_revision_and_basket_capability()
    {
        var sessionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var basketHash = TableGuestCredentialCrypto.HashBasketSession("basket-session");
        var fingerprint = new string('A', 64);
        var original = TableGuestCredentialCrypto.HashRoundRequest(
            sessionId, participantId, operationId, 5, basketHash, fingerprint);

        TableGuestCredentialCrypto.HashRoundRequest(sessionId, participantId, operationId, 6, basketHash, fingerprint)
            .Should().NotBe(original);
        TableGuestCredentialCrypto.HashRoundRequest(sessionId, participantId, operationId, 5,
            TableGuestCredentialCrypto.HashBasketSession("other-basket-session"), fingerprint)
            .Should().NotBe(original);
        TableGuestCredentialCrypto.HashRoundRequest(sessionId, participantId, operationId, 5,
            basketHash, new string('B', 64))
            .Should().NotBe(original);
        TableGuestCredentialCrypto.HashRoundRequest(sessionId, participantId, operationId, 5,
            basketHash, fingerprint.ToLowerInvariant()).Should().Be(original);
        TableGuestCredentialCrypto.HashBasketSession("basket-session")
            .Should().NotContain("basket-session");
    }
}
