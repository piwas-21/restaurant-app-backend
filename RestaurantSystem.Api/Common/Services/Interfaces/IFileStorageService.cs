using RestaurantSystem.Api.Common.Models;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

public interface IFileStorageService
{
    /// <summary>
    /// Uploads a file and returns the public URL
    /// </summary>
    /// <param name="file">The file to upload</param>
    /// <param name="folder">The folder/container to upload to (e.g., "products", "categories")</param>
    /// <param name="fileName">Optional custom filename. If null, generates unique name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The public URL of the uploaded file</returns>
    Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file from stream and returns the public URL
    /// </summary>
    /// <param name="stream">The file stream</param>
    /// <param name="folder">The folder/container to upload to</param>
    /// <param name="fileName">The filename including extension</param>
    /// <param name="contentType">The MIME content type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The public URL of the uploaded file</returns>
    Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file by its URL or key
    /// </summary>
    /// <param name="fileUrl">The file URL or key to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted successfully, false if not found</returns>
    Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a signed URL for private file access (if needed)
    /// </summary>
    /// <param name="fileKey">The file key/path</param>
    /// <param name="expirationTime">How long the URL should be valid</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signed URL for temporary access</returns>
    Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists
    /// </summary>
    /// <param name="fileUrl">The file URL or key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if file exists</returns>
    Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets file metadata
    /// </summary>
    /// <param name="fileUrl">The file URL or key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File metadata or null if not found</returns>
    Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default);
}
