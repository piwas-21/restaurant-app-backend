namespace RestaurantSystem.Api.Common.Services.Interfaces;

public interface IQRCodeService
{
    /// <summary>
    /// Generate QR code as PNG byte array
    /// </summary>
    byte[] GenerateQRCode(string data, int pixelsPerModule = 10);

    /// <summary>
    /// Generate a unique code for membership IDs
    /// </summary>
    string GenerateUniqueCode();

    /// <summary>
    /// Generate HMAC signature for QR code data
    /// </summary>
    string GenerateSignature(string data);

    /// <summary>
    /// Validate HMAC signature
    /// </summary>
    bool ValidateSignature(string data, string signature);
}
