using FluentAssertions;
using FluentValidation;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Behaviors;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.IntegrationTests.Common.Behaviors;

/// <summary>
/// Unit tests for <see cref="ValidationBehavior{TRequest, TResponse}"/>.
/// These exercise the behavior in isolation (no DI, no full host) so they
/// can run inside the same test project without database setup.
/// </summary>
public class ValidationBehaviorTests
{
    private record SampleRequest(string Name) : ICommand<string>;

    private class PassingValidator : AbstractValidator<SampleRequest>
    {
        public PassingValidator()
        {
            RuleFor(r => r.Name).NotEmpty();
        }
    }

    private class FailingValidator : AbstractValidator<SampleRequest>
    {
        public FailingValidator()
        {
            RuleFor(r => r.Name).Must(_ => false).WithMessage("always-fails");
        }
    }

    private static Task<string> HandlerOk() => Task.FromResult("handled");

    [Fact]
    public async Task Handle_WithNoValidators_InvokesHandler()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>(
            Array.Empty<IValidator<SampleRequest>>());

        var result = await behavior.Handle(new SampleRequest("x"), HandlerOk, CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_WithPassingValidator_InvokesHandler()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>(
            new IValidator<SampleRequest>[] { new PassingValidator() });

        var result = await behavior.Handle(new SampleRequest("ok"), HandlerOk, CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_WithFailingValidator_ThrowsBadRequestExceptionAndDoesNotInvokeHandler()
    {
        var handlerCalled = false;
        Task<string> NextWithFlag()
        {
            handlerCalled = true;
            return Task.FromResult("handled");
        }

        var behavior = new ValidationBehavior<SampleRequest, string>(
            new IValidator<SampleRequest>[] { new FailingValidator() });

        var act = async () => await behavior.Handle(
            new SampleRequest("anything"),
            NextWithFlag,
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BadRequestException>();
        ex.Which.Message.Should().Contain("always-fails");
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AggregatesMessagesFromMultipleFailingValidators()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>(
            new IValidator<SampleRequest>[]
            {
                new FailingValidator(),
                new PassingValidator(), // passes for "ok"
            });

        var act = async () => await behavior.Handle(
            new SampleRequest("ok"),
            HandlerOk,
            CancellationToken.None);

        await act.Should().ThrowAsync<BadRequestException>()
            .Where(e => e.Message.Contains("always-fails"));
    }
}
