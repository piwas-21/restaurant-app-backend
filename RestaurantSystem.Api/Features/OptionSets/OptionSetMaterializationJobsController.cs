using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.OptionSets;

[ApiController]
[Route("api/OptionSets/{id:guid}")]
public sealed class OptionSetMaterializationJobsController : ControllerBase
{
    private readonly IOptionSetMaterializationJobService _jobs;

    public OptionSetMaterializationJobsController(IOptionSetMaterializationJobService jobs)
    {
        _jobs = jobs;
    }

    [HttpPost("apply-jobs")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetMaterializationJobDto>>> CreateJob(
        Guid id,
        [FromBody] OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var job = await _jobs.CreateAsync(id, request, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { id, jobId = job.JobId },
            ApiResponse<OptionSetMaterializationJobDto>.SuccessWithData(job));
    }

    [HttpGet("apply-jobs/{jobId:guid}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetMaterializationJobDto>>> GetJob(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _jobs.GetAsync(id, jobId, cancellationToken);
        return Ok(ApiResponse<OptionSetMaterializationJobDto>.SuccessWithData(job));
    }

    [HttpPost("apply-jobs/{jobId:guid}/resume")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetMaterializationJobDto>>> ResumeJob(
        Guid id,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _jobs.ResumeAsync(id, jobId, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { id, jobId },
            ApiResponse<OptionSetMaterializationJobDto>.SuccessWithData(job));
    }
}
