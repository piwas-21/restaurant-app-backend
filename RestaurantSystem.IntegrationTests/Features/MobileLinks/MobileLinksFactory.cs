using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RestaurantSystem.IntegrationTests.Features.MobileDirectory;
using Sofra.MobileService;
using Sofra.MobileService.Links;

namespace RestaurantSystem.IntegrationTests.Features.MobileLinks;

internal sealed class MobileLinksFactory(bool enabled = true, string status = "active", int contract = 1,
    MobileAssociationSettings? associations = null) : WebApplicationFactory<MobileServiceProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            MobileDirectory = MobileDirectoryFixture.Settings([MobileDirectoryFixture.Alpha with { Status = status, ContractVersions = [contract] }]),
            MobileLinks = new MobileLinkSettings { Enabled = enabled, Origin = "https://links.example" },
            MobileAssociations = associations ?? new MobileAssociationSettings()
        }))));
}
