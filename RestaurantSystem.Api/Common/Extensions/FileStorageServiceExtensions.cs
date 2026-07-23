using Amazon.S3;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class FileStorageServiceExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var fileStorageSettings = configuration.GetSection(FileStorageSettings.SectionName).Get<FileStorageSettings>()
                                 ?? new FileStorageSettings();

        services.Configure<FileStorageSettings>(configuration.GetSection(FileStorageSettings.SectionName));

        // Resize/recompress raster uploads at the storage seam, regardless of provider.
        services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();

        switch (fileStorageSettings.Provider.ToLower())
        {
            case "s3":
                services.AddS3FileStorage(configuration);
                break;
            default:
                services.AddLocalFileStorage();
                break;
        }
        return services;
    }

    private static void AddS3FileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var awsSettings = configuration.GetSection(AWSSettings.SectionName).Get<AWSSettings>();
        if (awsSettings == null)
            throw new InvalidOperationException("AWS settings not found in configuration");

        services.Configure<AWSSettings>(configuration.GetSection(AWSSettings.SectionName));

        // Register AWS S3 client
        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(awsSettings.Region)
            };

            return new AmazonS3Client(awsSettings.AccessKey, awsSettings.SecretKey, config);
        });

        services.AddScoped<S3FileStorageService>();
        services.AddScoped<IFileStorageService>(sp => new ImageProcessingFileStorageService(
            sp.GetRequiredService<S3FileStorageService>(),
            sp.GetRequiredService<IImageProcessor>()));
    }

    private static void AddLocalFileStorage(this IServiceCollection services)
    {
        services.AddScoped<LocalFileStorageService>();
        services.AddScoped<IFileStorageService>(sp => new ImageProcessingFileStorageService(
            sp.GetRequiredService<LocalFileStorageService>(),
            sp.GetRequiredService<IImageProcessor>()));
    }
}
