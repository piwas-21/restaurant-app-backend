using QRCoder;
using RestaurantSystem.Api.Common.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantSystem.Api.Common.Services;

public class QRCodeService : IQRCodeService
{
    private readonly string _secretKey;

    public QRCodeService(IConfiguration configuration)
    {
        _secretKey = configuration["QRCode:SecretKey"] ?? "default-secret-key-change-in-production";
    }

    public byte[] GenerateQRCode(string data, int pixelsPerModule = 10)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(pixelsPerModule);
    }

    public string GenerateUniqueCode()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
    }

    public string GenerateSignature(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    public bool ValidateSignature(string data, string signature)
    {
        var expectedSignature = GenerateSignature(data);
        return expectedSignature == signature;
    }
}
