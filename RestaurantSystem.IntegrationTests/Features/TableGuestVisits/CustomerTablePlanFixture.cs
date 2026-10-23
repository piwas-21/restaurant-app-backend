using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

public abstract class CustomerTablePlanFixture(DatabaseFixture fixture) : CustomerTablePaymentPreparationFixture(fixture)
{
    protected const string PlanPath = "/api/customer-mobile/table-payments/plans/";
    protected bool FailAfterPlan { get; set; }
    protected bool PlanObserved { get; private set; }
    protected Action? AfterPlanProof { get; set; }
    protected Func<AccountSharePlanReviewDto, AccountSharePlanReviewDto>? ReviewChange { get; set; }
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IAccountEqualSharePlanService>();
        services.AddScoped<IAccountEqualSharePlanService>(provider => new PlanHook(
            ActivatorUtilities.CreateInstance<AccountEqualSharePlanService>(provider), value => ReviewChange?.Invoke(value) ?? value, () =>
            { PlanObserved = true; if (FailAfterPlan) throw new ConflictException("Fixture failure after shared plan creation."); }));
        services.RemoveAll<ICustomerTablePlanProofs>();
        services.AddScoped<ICustomerTablePlanProofs>(provider => new ProofHook(
            ActivatorUtilities.CreateInstance<CustomerTablePlanProofs>(provider), () => AfterPlanProof?.Invoke()));
    }
    protected async Task<CustomerTablePlanIntentDto> PlanIntentAsync(int count = 3, IReadOnlyList<long>? amounts = null)
    {
        var payment = await PaymentIntentAsync();
        return new(1, payment.TenantId, Guid.NewGuid(), payment.ServiceSessionId, payment.ExpectedAccountRevision,
            count, amounts ?? [], null);
    }
    protected async Task<CustomerTablePlanPreparedDto> PreparePlanAsync(CustomerTablePlanIntentDto intent)
    {
        var response = await SendAsync(PlanPath + "prepare", intent);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePlanPreparedDto>>(JsonOptions))!.Data!;
    }
    protected static async Task<CustomerTablePlanResolutionDto> PlanResolutionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerTablePlanResolutionDto>>(JsonOptions))!.Data!;
    }
    protected async Task NoPlansAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(0);
        (await context.Set<CustomerTablePlanOperation>().CountAsync()).Should().Be(0);
        await NoPaymentEffectsAsync();
    }
    private sealed class PlanHook(IAccountEqualSharePlanService inner, Func<AccountSharePlanReviewDto, AccountSharePlanReviewDto> review, Action after) : IAccountEqualSharePlanService
    {
        public async Task<AccountSharePlanReviewDto> ReviewGuestAsync(Guid session, string? token,
            CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken) =>
            review(await inner.ReviewGuestAsync(session, token, request, cancellationToken));
        public Task<AccountEqualSharePlanDto> CreateAsync(Guid session, CreateAccountEqualSharePlanRequest request,
            CancellationToken cancellationToken) => inner.CreateAsync(session, request, cancellationToken);
        public async Task<AccountEqualSharePlanDto> CreateGuestAsync(Guid session, string? token,
            CreateAccountEqualSharePlanRequest request, CancellationToken cancellationToken)
        { var plan = await inner.CreateGuestAsync(session, token, request, cancellationToken); after(); return plan; }
    }
    private sealed class ProofHook(ICustomerTablePlanProofs inner, Action after) : ICustomerTablePlanProofs
    {
        public CustomerTablePlanProofDto Create(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner, CustomerTablePlanBinding binding)
        { var proof = inner.Create(request, owner, binding); after(); return proof; }
        public CustomerTablePlanBinding Require(CustomerTablePlanProofDto proof, CustomerTablePaymentOwner owner) => inner.Require(proof, owner);
        public void RequireInput(CustomerTablePlanIntentDto request, CustomerTablePaymentOwner owner,
            CustomerTablePlanBinding binding, CustomerTablePlanProofDto proof) => inner.RequireInput(request, owner, binding, proof);
    }
}
