namespace RestaurantSystem.Channels.Api;

public interface ISandboxCrypto
{
    string Protect(string plaintext, string purpose);
    string Unprotect(string ciphertext, string purpose);
    string RandomToken();
    string Hash(string value);
    bool MatchesHash(string value, string expectedHash);
}
