using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueImportSessionRulesTests
{
    [Fact]
    public void Create_request_rejects_template_ids_that_exceed_the_persisted_limit()
    {
        var request = new CreateCatalogueImportSessionRequest
        {
            TemplateId = new string('a', 121),
            Revision = 1,
            Locale = "en",
            IdempotencyKey = "test",
            SelectedTemplateIds = []
        };

        var act = () => CatalogueImportSessionRules.ValidateCreateRequest(request);

        act.Should().Throw<BadRequestException>();
    }
}
