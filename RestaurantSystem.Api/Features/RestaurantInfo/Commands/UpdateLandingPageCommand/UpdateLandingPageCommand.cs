using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateLandingPageCommand;

/// <summary>
/// Fully replaces landing-page overrides. Omitted locale rows are removed; blank copy becomes null
/// so the client uses its bundled translation fallback. A blank language means the tenant default.
/// </summary>
public record UpdateLandingPageCommand(
    string BackgroundMode,
    List<UpdateLandingPageContentDto> Content) : ICommand<ApiResponse<LandingPageDto>>;

public class UpdateLandingPageCommandHandler
    : ICommandHandler<UpdateLandingPageCommand, ApiResponse<LandingPageDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailLanguageResolver _languages;
    private readonly IConfiguration _configuration;

    public UpdateLandingPageCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IEmailLanguageResolver languages,
        IConfiguration configuration)
    {
        _context = context;
        _currentUserService = currentUserService;
        _languages = languages;
        _configuration = configuration;
    }

    public async Task<ApiResponse<LandingPageDto>> Handle(
        UpdateLandingPageCommand command, CancellationToken cancellationToken)
    {
        var info = await _context.RestaurantInfo
            .Include(item => item.LandingContents)
            .FirstOrDefaultAsync(cancellationToken);

        if (info is null)
        {
            throw new NotFoundException("Restaurant info has not been initialised.");
        }

        var backgroundMode = ParseBackgroundMode(command.BackgroundMode);
        if (backgroundMode == LandingBackgroundMode.Custom && string.IsNullOrWhiteSpace(info.InteriorImageUrl))
        {
            throw new BadRequestException("Upload an interior image before selecting custom background mode.");
        }

        var incoming = command.Content.ToDictionary(
            item => NormalizeLanguage(item.LanguageCode),
            item => item,
            StringComparer.Ordinal);

        foreach (var existing in info.LandingContents.Where(item => !incoming.ContainsKey(item.LanguageCode)).ToList())
        {
            info.LandingContents.Remove(existing);
            _context.RestaurantLandingContents.Remove(existing);
        }

        foreach (var (languageCode, content) in incoming)
        {
            var existing = info.LandingContents.FirstOrDefault(item => item.LanguageCode == languageCode);
            if (existing is null)
            {
                existing = new RestaurantLandingContent { LanguageCode = languageCode, CreatedBy = _currentUserService.GetAuditIdentifier() };
                info.LandingContents.Add(existing);
            }

            existing.HeroEyebrow = NullWhenBlank(content.HeroEyebrow);
            existing.WelcomeTitle = NullWhenBlank(content.WelcomeTitle);
            existing.WelcomeBody = NullWhenBlank(content.WelcomeBody);
            existing.StoryTitle = NullWhenBlank(content.StoryTitle);
            existing.StoryBody = NullWhenBlank(content.StoryBody);
        }

        info.LandingBackgroundMode = backgroundMode;
        info.UpdatedAt = DateTime.UtcNow;
        info.UpdatedBy = _currentUserService.GetAuditIdentifier();
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<LandingPageDto>.SuccessWithData(
            RestaurantLandingPageMapper.ToDto(info, _configuration["AWS:S3:BaseUrl"]));
    }

    private string NormalizeLanguage(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode) ? _languages.TenantDefault : LanguageCode.Normalize(languageCode)!;

    private static LandingBackgroundMode ParseBackgroundMode(string value) =>
        Enum.Parse<LandingBackgroundMode>(value, ignoreCase: true);

    private static string? NullWhenBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
