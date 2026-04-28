using Amazon.S3;
using Amazon.S3.Model;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.Api.Common.Services;

public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _baseUrl;

    public S3FileStorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["AWS:S3:BucketName"]!;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!; // e.g., "https://your-bucket.s3.amazonaws.com"
    }

    public async Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            throw new ArgumentException("File is empty", nameof(file));

        fileName ??= GenerateUniqueFileName(file.FileName);
        var key = $"{folder.Trim('/')}/{fileName}";

        using var stream = file.OpenReadStream();
        return await UploadFileAsync(stream, folder, fileName, file.ContentType, cancellationToken);
    }

    public async Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var key = $"{folder.Trim('/')}/{fileName}";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            CannedACL = S3CannedACL.NoACL,
            Metadata =
                {
                    ["uploaded-at"] = DateTime.UtcNow.ToString("O"),
                    ["original-name"] = fileName
                }
        };

        try
        {
            var response = await _s3Client.PutObjectAsync(request, cancellationToken);
            return $"{key}";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to upload file to S3: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = ExtractKeyFromUrl(fileUrl);

            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = fileKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expirationTime)
        };

        return await Task.FromResult(_s3Client.GetPreSignedURL(request));
    }

    public async Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = ExtractKeyFromUrl(fileUrl);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = ExtractKeyFromUrl(fileUrl);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectMetadataAsync(request, cancellationToken);

            var metadata = new Dictionary<string, string>();
            foreach (var item in response.Metadata.Keys)
            {
                metadata[item] = response.Metadata[item];
            }

            return new FileMetadata
            {
                FileName = Path.GetFileName(key),
                ContentType = response.Headers.ContentType,
                Size = response.Headers.ContentLength,
                LastModified = response.LastModified,
                Url = fileUrl,
                Metadata = metadata
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private string ExtractKeyFromUrl(string fileUrl)
    {
        // Extract the S3 key from the full URL
        var uri = new Uri(fileUrl);
        return uri.AbsolutePath.TrimStart('/');
    }

    private static string GenerateUniqueFileName(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var guid = Guid.NewGuid().ToString("N")[..8]; // First 8 chars of GUID
        return $"{timestamp}_{guid}{extension}";
    }
}
