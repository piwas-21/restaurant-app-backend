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

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInteriorImageCommand;

/// <summary>
/// Uploads the restaurant's custom landing-background image and points the
/// <see cref="Domain.Entities.RestaurantInfo"/> singleton at it. Admin-only.
/// </summary>
/// <remarks>
/// A deliberate copy of <c>UpdateRestaurantLogoCommand</c> — same allowlist, same
/// <c>branding/</c> folder, same delete-after-commit file lifecycle — because the photo is the
/// same KIND of thing: runtime tenant data, uploaded by the tenant admin, with no build-time
/// or registry involvement (SOFRA-ONBOARDING-PLAN O6). The one difference is that there is no
/// variant: a restaurant has one interior photo, so the route carries no discriminator.
/// </remarks>
public record UpdateRestaurantInteriorImageCommand(IFormFile? Image)
    : ICommand<ApiResponse<RestaurantInfoDto>>;

public class UpdateRestaurantInteriorImageCommandHandler
    : ICommandHandler<UpdateRestaurantInteriorImageCommand, ApiResponse<RestaurantInfoDto>>
{
    private const string ImageFolder = "branding";

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateRestaurantInteriorImageCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly FileStorageSettings _fileStorageSettings;

    public UpdateRestaurantInteriorImageCommandHandler(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        ICurrentUserService currentUserService,
        ILogger<UpdateRestaurantInteriorImageCommandHandler> logger,
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
        UpdateRestaurantInteriorImageCommand command, CancellationToken cancellationToken)
    {
        var image = command.Image;
        if (!ImageUploadRules.IsAcceptable(image, _fileStorageSettings, out var rejection))
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

        var previousUrl = info.InteriorImageUrl;

        try
        {
            var uploadedUrl = await _fileStorageService.UploadFileAsync(
                image,
                ImageFolder,
                cancellationToken: cancellationToken);

            info.InteriorImageUrl = uploadedUrl;
            info.UpdatedAt = DateTime.UtcNow;
            info.UpdatedBy = _currentUserService.GetAuditIdentifier();

            await _context.SaveChangesAsync(cancellationToken);

            // Delete the replaced file only after the new URL is committed — the same order the
            // logo handler uses, and for the same reason: the other order loses the old photo
            // whenever the write fails, leaving the page pointing at a file that is gone.
            if (!string.IsNullOrEmpty(previousUrl))
            {
                await _fileStorageService.DeleteFileAsync(previousUrl, cancellationToken);
            }

            _logger.LogInformation("Restaurant interior image updated");

            return ApiResponse<RestaurantInfoDto>.SuccessWithData(
                RestaurantInfoMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]),
                "Interior image updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload interior image");
            return ApiResponse<RestaurantInfoDto>.Failure("Failed to upload interior image");
        }
    }
}
