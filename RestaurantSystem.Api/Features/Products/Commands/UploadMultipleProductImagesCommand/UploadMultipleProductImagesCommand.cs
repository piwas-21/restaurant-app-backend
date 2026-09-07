using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;

/// <summary>
/// Stores a batch of product images, keeping every per-file rejection reason and returning it to
/// the caller.
/// </summary>
/// <remarks>
/// The response contract, decided in Track F1b after a tenant lost weeks of photo uploads to it.
/// <b>Nothing stored</b> ⇒ <c>Failure</c> with one <c>Errors</c> entry per rejected file; it used to
/// be a <c>SuccessWithData</c> carrying an empty list and <c>errors: null</c>, so a total failure was
/// indistinguishable from a no-op on the wire and the reason lived only in the server log.
/// <b>Some stored, some rejected</b> ⇒ still a success envelope (the stored images ARE saved and the
/// client must render them) but with <c>Errors</c> populated, so the user can be told WHICH photo did
/// not make it. <b>All stored</b> ⇒ success, <c>Errors</c> null. The HTTP status stays 200 in every
/// case: the controller is a plain <c>Ok(result)</c> like every other endpoint here, and clients
/// branch on the envelope's <c>success</c> flag.
/// </remarks>
public record UploadMultipleProductImagesCommand(
    Guid ProductId,
    List<IFormFile> Images
) : ICommand<ApiResponse<List<ProductImageDto>>>;
