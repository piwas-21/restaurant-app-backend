using System.Diagnostics.CodeAnalysis;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// The allowlist an uploaded image must satisfy before it reaches
/// <see cref="Services.Interfaces.IFileStorageService"/>: non-empty, within
/// <see cref="FileStorageSettings.MaxFileSizeBytes"/>, and both its extension and its declared
/// MIME type present in the configured allowlists.
/// </summary>
/// <remarks>
/// Extracted from <c>UpdateCategoryImageCommandHandler</c> and
/// <c>UploadProductImageCommandHandler</c>, whose copies of this block were byte-identical apart
/// from the <c>ApiResponse&lt;T&gt;</c> they wrapped the message in. The rules are a security
/// boundary, so a third hand-written copy is the failure mode worth designing out: the day the
/// allowlist tightens, one forgotten copy keeps accepting what the others reject.
/// <para>
/// Extension and MIME are deliberately checked separately rather than cross-referenced — a file
/// named <c>.png</c> that declares <c>text/html</c> has to fail, and so does an <c>.exe</c> that
/// declares <c>image/png</c>. Neither is authoritative about content; the real decode happens in
/// <c>ImageSharpImageProcessor</c> at the storage seam, which stores anything it cannot decode
/// untouched rather than rejecting it. This is the cheap gate in front of that.
/// </para>
/// <para>
/// <c>UploadMultipleProductImagesCommandHandler</c> used to keep its own copy — the third one this
/// class exists to prevent — on the reasoning that a bulk upload accumulates a per-file error and
/// continues rather than returning on the first failure. It does, but that needs a reason per file,
/// which is exactly what <c>rejection</c> is: the bulk handler now calls this per file and prefixes
/// the answer with the file's name (Track F1b). Its copy had already drifted — the messages named
/// the file but not the allowlist, and it read the allowlist from <c>IConfiguration</c> with its own
/// hardcoded fallbacks instead of <see cref="FileStorageSettings"/>.
/// </para>
/// </remarks>
public static class ImageUploadRules
{
    /// <summary>
    /// True when <paramref name="file"/> may be stored; otherwise false with
    /// <paramref name="rejection"/> set to the user-facing reason.
    /// </summary>
    /// <remarks>
    /// A boolean with an out-message rather than a thrown exception, because every caller reports
    /// it as <c>ApiResponse&lt;T&gt;.Failure(...)</c> — a rejected upload is a 400-shaped answer to
    /// the user, not an exceptional condition. The <c>NotNullWhen</c> annotations carry both halves
    /// of that into flow analysis, so a caller cannot use an unvalidated file or read a rejection
    /// that was never set.
    /// </remarks>
    public static bool IsAcceptable(
        [NotNullWhen(true)] IFormFile? file,
        FileStorageSettings settings,
        [NotNullWhen(false)] out string? rejection)
    {
        if (file is null || file.Length == 0)
        {
            rejection = "No image file provided";
            return false;
        }

        if (file.Length > settings.MaxFileSizeBytes)
        {
            rejection = $"File size exceeds maximum allowed size of {settings.MaxFileSizeBytes / (1024 * 1024)}MB";
            return false;
        }

        var allowedExtensions = settings.AllowedExtensions;
        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(fileExtension))
        {
            rejection = $"File type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}";
            return false;
        }

        // `IFormFile.ContentType` reads straight out of the header dictionary and is null when the
        // part carries no Content-Type, so this cannot be dereferenced unguarded. An absent type
        // then falls through to the allowlist check and is rejected, which is the safe direction.
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!settings.AllowedMimeTypes.Contains(contentType))
        {
            rejection = "Invalid image MIME type";
            return false;
        }

        rejection = null;
        return true;
    }
}
