using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CreateAccountEqualSharePlanCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CreateAccountPaymentQuoteCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ReleaseAccountPaymentCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.ReserveAccountPaymentCommand;
using RestaurantSystem.Api.Features.AccountPayments.Commands.CaptureAccountPaymentCommand;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountEqualSharePlanQuery;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentAccountQuery;
using RestaurantSystem.Api.Features.AccountPayments.Queries.GetAccountPaymentOperationQuery;

namespace RestaurantSystem.Api.Features.AccountPayments;

[ApiController]
[Route("api/table-service-sessions/{serviceSessionId:guid}/account-payments")]
[RequireAdminOrCashier]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountPaymentsController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AccountPaymentAccountDto>>> GetAccount(Guid serviceSessionId) =>
        Ok(await mediator.SendQuery(new GetAccountPaymentAccountQuery(serviceSessionId)));

    [HttpPost("quotes")]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> CreateQuote(
        Guid serviceSessionId, [FromBody] CreateAccountPaymentQuoteRequest request) =>
        Ok(await mediator.SendCommand(new CreateAccountPaymentQuoteCommand(serviceSessionId, request)));

    [HttpPost("equal-share-plans")]
    public async Task<ActionResult<ApiResponse<AccountEqualSharePlanDto>>> CreateEqualSharePlan(
        Guid serviceSessionId, [FromBody] CreateAccountEqualSharePlanRequest request) =>
        Ok(await mediator.SendCommand(new CreateAccountEqualSharePlanCommand(serviceSessionId, request)));

    [HttpGet("operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> GetOperation(
        Guid serviceSessionId, Guid operationId) =>
        Ok(await mediator.SendQuery(new GetAccountPaymentOperationQuery(serviceSessionId, operationId)));

    [HttpPost("operations/{operationId:guid}/reserve")]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> Reserve(
        Guid serviceSessionId, Guid operationId, [FromBody] ReserveAccountPaymentRequest request) =>
        Ok(await mediator.SendCommand(new ReserveAccountPaymentCommand(serviceSessionId, operationId, request)));

    [HttpPost("operations/{operationId:guid}/release")]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> Release(
        Guid serviceSessionId, Guid operationId, [FromBody] ReleaseAccountPaymentRequest request) =>
        Ok(await mediator.SendCommand(new ReleaseAccountPaymentCommand(serviceSessionId, operationId, request)));

    [HttpGet("equal-share-plans/operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<AccountEqualSharePlanDto>>> GetEqualSharePlan(
        Guid serviceSessionId, Guid operationId) =>
        Ok(await mediator.SendQuery(new GetAccountEqualSharePlanQuery(serviceSessionId, operationId)));

    [HttpPost("operations/{operationId:guid}/collect")]
    public async Task<ActionResult<ApiResponse<AccountPaymentOperationDto>>> CaptureManual(
        Guid serviceSessionId, Guid operationId, [FromBody] CaptureAccountPaymentRequest request) =>
        Ok(await mediator.SendCommand(new CaptureAccountPaymentCommand(serviceSessionId, operationId, request)));
}
