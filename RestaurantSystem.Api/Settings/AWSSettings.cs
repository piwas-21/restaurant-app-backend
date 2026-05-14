
namespace RestaurantSystem.Api.Settings;

public class AWSSettings
{
    public const string SectionName = "AWS";

    public string AccessKey { get; set; } = null!;
    public string SecretKey { get; set; } = null!;
    public string Region { get; set; } = "us-east-1";

    public S3Settings S3 { get; set; } = new();

}
public class S3Settings
{
    public string BucketName { get; set; } = null!;
    public string BaseUrl { get; set; } = null!;
}
