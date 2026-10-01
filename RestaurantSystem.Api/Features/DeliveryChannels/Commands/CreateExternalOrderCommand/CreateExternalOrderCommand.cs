using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.CreateExternalOrderCommand;

public sealed record CreateExternalOrderCommand(ExternalOrderRequest Request) : ICommand<ExternalOrderImportDto>;

public sealed class CreateExternalOrderCommandHandler(IExternalOrderImporter importer)
    : ICommandHandler<CreateExternalOrderCommand, ExternalOrderImportDto>
{
    public Task<ExternalOrderImportDto> Handle(CreateExternalOrderCommand command, CancellationToken cancellationToken)
        => importer.ImportAsync(command.Request, cancellationToken);
}
