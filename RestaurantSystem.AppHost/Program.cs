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

var certPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".aspnet", "https", "aspnetdev.pem");


builder.AddJavaScriptApp("frontend", "../../restaurant-app-frontend", runScriptName: "dev")
       .WithReference(api)
       .WithEnvironment("NEXT_PUBLIC_API_URL", api.GetEndpoint("http"))
       .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
       .WithEnvironment("NODE_EXTRA_CA_CERTS", certPath)               // trust .NET dev cert
       .WaitFor(api)
       .WithHttpEndpoint(port:3000,env: "PORT")
       .WithExternalHttpEndpoints()
       .PublishAsDockerFile();

builder.Build().Run();
