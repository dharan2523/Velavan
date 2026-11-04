using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Api.Authorization;
using VelavanDemo.Application.Common;
using VelavanDemo.Application.Users;
using VelavanDemo.Application.Users.Models;

namespace VelavanDemo.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AuthorizationPolicies.AdminOrSuperAdmin)]
public sealed class AdminUsersController : ApiControllerBase
{
    private readonly IUserManagementService _userManagementService;

    public AdminUsersController(IUserManagementService userManagementService)
    {
        _userManagementService = userManagementService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserResponse>>> GetUsers(
        [FromQuery] UserQuery query,
        CancellationToken cancellationToken)
    {
        var users = await _userManagementService.GetUsersAsync(query, cancellationToken);
        return Ok(users);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var result = await _userManagementService.GetUserAsync(id, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = CurrentUserId;

        if (actingUserId is null)
        {
            return Unauthorized();
        }

        var result = await _userManagementService.CreateUserAsync(
            request,
            actingUserId.Value,
            IsSuperAdmin,
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToErrorResult(result);
        }

        return CreatedAtAction(
            nameof(GetUser),
            new { id = result.Value!.Id },
            result.Value);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUser(
        int id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = CurrentUserId;

        if (actingUserId is null)
        {
            return Unauthorized();
        }

        var result = await _userManagementService.UpdateUserAsync(
            id,
            request,
            actingUserId.Value,
            IsSuperAdmin,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPut("{id:int}/roles")]
    [Authorize(Policy = AuthorizationPolicies.SuperAdminOnly)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUserRoles(
        int id,
        [FromBody] UpdateUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = CurrentUserId;

        if (actingUserId is null)
        {
            return Unauthorized();
        }

        var result = await _userManagementService.UpdateUserRolesAsync(
            id,
            request,
            actingUserId.Value,
            IsSuperAdmin,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUserStatus(
        int id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var actingUserId = CurrentUserId;

        if (actingUserId is null)
        {
            return Unauthorized();
        }

        var result = await _userManagementService.UpdateUserStatusAsync(
            id,
            request,
            actingUserId.Value,
            IsSuperAdmin,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToErrorResult(result);
    }

    private IActionResult ToErrorResult<T>(OperationResult<T> result)
    {
        return result.Error switch
        {
            OperationError.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Resource not found",
                detail: result.Message),
            OperationError.DuplicateUsername => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate username",
                detail: result.Message),
            OperationError.DuplicateEmail => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Duplicate email",
                detail: result.Message),
            OperationError.InvalidRole => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid role",
                detail: result.Message),
            OperationError.Validation => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: result.Message),
            OperationError.Forbidden => Forbid(),
            OperationError.LastSuperAdmin => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Super Admin required",
                detail: result.Message),
            OperationError.SelfModification => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Operation not allowed",
                detail: result.Message),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Request failed",
                detail: result.Message)
        };
    }
}
