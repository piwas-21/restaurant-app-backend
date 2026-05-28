using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// PR #67 review regression guard. The non-generic dispatch path
/// (<see cref="CustomMediator.SendCommand{TResult}(ICommand{TResult}, CancellationToken)"/>)
/// invokes pipeline behaviors via <c>MethodInfo.Invoke</c>, which
/// wraps any synchronous exception in <see cref="TargetInvocationException"/>.
/// Without an unwrap, <see cref="BadRequestException"/> / <see cref="NotFoundException"/>
/// reach the global exception middleware as TargetInvocationException and map
/// to 500 instead of 400/404. This test pins the unwrap behaviour.
/// </summary>
public class CustomMediatorExceptionUnwrapTests
{
    public record TestCommand : ICommand<string>;

    public class TestCommandHandler : ICommandHandler<TestCommand, string>
    {
        public Task<string> Handle(TestCommand command, CancellationToken cancellationToken)
            => Task.FromResult("ok");
    }

    /// <summary>
    /// Behavior that throws synchronously before returning a Task — this is
    /// the exact shape of <c>ValidationBehavior</c> when validation fails.
    /// Throwing synchronously triggers <c>MethodInfo.Invoke</c>'s
    /// TargetInvocationException wrapping.
    /// </summary>
    public class ThrowingBehavior : IPipelineBehavior<TestCommand, string>
    {
        public Task<string> Handle(
            TestCommand request,
            RequestHandlerDelegate<string> next,
            CancellationToken cancellationToken)
        {
            throw new BadRequestException("synchronous validation failure");
        }
    }

    [Fact]
    public async Task NonGenericDispatch_BehaviorThrowsSynchronously_UnwrapsTargetInvocationException()
    {
        var services = new ServiceCollection();
        services.AddTransient<ICommandHandler<TestCommand, string>, TestCommandHandler>();
        services.AddTransient<IPipelineBehavior<TestCommand, string>, ThrowingBehavior>();
        services.AddTransient<CustomMediator>();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<CustomMediator>();
        ICommand<string> command = new TestCommand();

        // Act + Assert — must be BadRequestException, NOT TargetInvocationException.
        var act = async () => await mediator.SendCommand(command);
        await act.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("synchronous validation failure");
    }
}
