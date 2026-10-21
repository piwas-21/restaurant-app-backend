using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public abstract class CustomerTablePaymentAccountFixture(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    protected Func<Task>? AfterRead { get; set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        Features.SetupGet(value => value.TableAccountPaymentsV1).Returns(true);
        services.RemoveAll<IAccountPaymentAccountReader>();
        services.AddScoped<IAccountPaymentAccountReader>(provider => new ReadHook(
            ActivatorUtilities.CreateInstance<AccountPaymentAccountReader>(provider), () => AfterRead?.Invoke() ?? Task.CompletedTask));
    }

    protected Task<HttpResponseMessage> AccountResponseAsync(string? schema = "1", Guid? tenant = null,
        Guid? visit = null, string? session = null)
    {
        var query = $"tenantId={tenant ?? NativeTenantId:D}&serviceSessionId={visit ?? Visit.ServiceSessionId:D}";
        if (schema is not null) query += $"&schemaVersion={schema}";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/customer-mobile/table-payments/account?" + query);
        request.Headers.Add("X-Session-Id", session ?? Session);
        return Client.SendAsync(request);
    }

    private sealed class ReadHook(IAccountPaymentAccountReader inner, Func<Task> afterRead) : IAccountPaymentAccountReader
    {
        public Task<AccountPaymentAccountDto> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
            inner.GetAsync(sessionId, cancellationToken);

        public async Task<AccountPaymentAccountDto> GetGuestAsync(Guid sessionId, string? participantCredential,
            CancellationToken cancellationToken)
        {
            var result = await inner.GetGuestAsync(sessionId, participantCredential, cancellationToken);
            await afterRead();
            return result;
        }
    }
}
