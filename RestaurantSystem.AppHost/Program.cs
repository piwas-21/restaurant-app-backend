// See https://aka.ms/new-console-template for more information

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
                      .AddDatabase("restaurantdb");

var redis = builder.AddRedis("redis");

var api = builder.AddProject<Projects.RestaurantSystem_Api>("restaurantapi")
                 .WithReference(postgres)
                 .WithReference(redis)
                 .WaitFor(postgres)
                 .WaitFor(redis);

// .NET dev-cert path for Node to trust the API's https endpoint. Optional —
// fresh machines / CI runners won't have it, and Node logs a noisy
// "Ignoring extra certs from ... load failed" if NODE_EXTRA_CA_CERTS points
// at a missing file. Only set the env var when the cert actually exists.
var certPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".aspnet", "https", "aspnetdev.pem");

var frontend = builder.AddJavaScriptApp("frontend", "../../restaurant-app-frontend", runScriptName: "dev")
       .WithReference(api)
       .WithEnvironment("NEXT_PUBLIC_API_URL", api.GetEndpoint("http"))
       .WaitFor(api)
       .WithHttpEndpoint(port: 3000, env: "PORT")
       .WithExternalHttpEndpoints()
       .PublishAsDockerFile();

if (File.Exists(certPath))
{
    frontend.WithEnvironment("NODE_EXTRA_CA_CERTS", certPath);
}

builder.Build().Run();
