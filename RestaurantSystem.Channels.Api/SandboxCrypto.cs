using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxCrypto(IOptions<SandboxConsoleSettings> options) : ISandboxCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string plaintext, string purpose)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[bytes.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.EncryptionKey), TagSize);
        aes.Encrypt(nonce, bytes, cipher, tag, Encoding.UTF8.GetBytes(purpose));
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string ciphertext, string purpose)
    {
        var bytes = Convert.FromBase64String(ciphertext);
        var plain = new byte[bytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(Convert.FromBase64String(options.Value.EncryptionKey), TagSize);
        aes.Decrypt(bytes.AsSpan(0, NonceSize), bytes.AsSpan(NonceSize + TagSize),
            bytes.AsSpan(NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(purpose));
        return Encoding.UTF8.GetString(plain);
    }

    public string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public bool MatchesHash(string value, string expectedHash)
        => expectedHash.Length == 64 && expectedHash.All(Uri.IsHexDigit)
            && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(value)), Convert.FromHexString(expectedHash));
}
