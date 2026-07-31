using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantLogoCommand;

/// <summary>
/// Uploads the tenant's own logo (SOFRA-ONBOARDING-PLAN O6) and points the
/// <see cref="Domain.Entities.RestaurantInfo"/> singleton at it. Admin-only.
/// </summary>
/// <remarks>
/// Mirrors <c>UpdateCategoryImageCommand</c>'s allowlists and stores under <c>branding/</c>
/// rather than <c>categories/{id}</c>, but deliberately INVERTS its file lifecycle: the category
/// handler deletes the old file before uploading, this one deletes it after the new URL is
/// committed. See the comment at the delete call for why.
/// </remarks>
public record UpdateRestaurantLogoCommand(
    LogoVariant Variant,
    IFormFile? Logo
) : ICommand<ApiResponse<RestaurantInfoDto>>;

public class UpdateRestaurantLogoCommandHandler
    : ICommandHandler<UpdateRestaurantLogoCommand, ApiResponse<RestaurantInfoDto>>
{
    private const string LogoFolder = "branding";

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateRestaurantLogoCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileStorageSettings _fileStorageSettings;

    public UpdateRestaurantLogoCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UpdateRestaurantLogoCommandHandler> logger,
        IConfiguration configuration,
        IOptions<FileStorageSettings> fileStorageSettings)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _currentUserService = currentUserService;
        _logger = logger;
        _configuration = configuration;
        _fileStorageSettings = fileStorageSettings.Value;
    }

    public async Task<ApiResponse<RestaurantInfoDto>> Handle(
        UpdateRestaurantLogoCommand command, CancellationToken cancellationToken)
    {
        var logo = command.Logo;
        if (!ImageUploadRules.IsAcceptable(logo, _fileStorageSettings, out var rejection))
        {
            return ApiResponse<RestaurantInfoDto>.Failure(rejection);
        }

        var info = await _context.RestaurantInfo
            .Include(r => r.PhoneNumbers)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var previousUrl = Read(info, command.Variant);

        try
        {
            var uploadedUrl = await _fileStorageService.UploadFileAsync(
                logo,
                LogoFolder,
                cancellationToken: cancellationToken);

            Write(info, command.Variant, uploadedUrl);
            info.UpdatedAt = DateTime.UtcNow;
            info.UpdatedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);

            // Delete the replaced file only after the new URL is committed. The other order
            // loses the old logo whenever the write fails, leaving the header pointing at a
            // file that no longer exists — strictly worse than the orphan this risks.
            await DeleteIfPresentAsync(previousUrl, cancellationToken);

            _logger.LogInformation(
                "Restaurant {Variant} logo updated", command.Variant);

            return ApiResponse<RestaurantInfoDto>.SuccessWithData(
                RestaurantInfoMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]),
                "Logo updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload {Variant} logo", command.Variant);
            return ApiResponse<RestaurantInfoDto>.Failure("Failed to upload logo");
        }
    }

    private async Task DeleteIfPresentAsync(string? url, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(url))
        {
            await _fileStorageService.DeleteFileAsync(url, cancellationToken);
        }
    }

    internal static string? Read(Domain.Entities.RestaurantInfo info, LogoVariant variant) =>
        variant == LogoVariant.Dark ? info.LogoDarkUrl : info.LogoUrl;

    internal static void Write(Domain.Entities.RestaurantInfo info, LogoVariant variant, string? url)
    {
        if (variant == LogoVariant.Dark)
        {
            info.LogoDarkUrl = url;
        }
        else
        {
            info.LogoUrl = url;
        }
    }
}
