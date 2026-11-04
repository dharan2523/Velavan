using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Application.Authentication;
using VelavanDemo.Application.Authentication.Models;
using VelavanDemo.Application.Common;

namespace VelavanDemo.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);

        if (result.Succeeded)
        {
            return Ok(result.Response);
        }

        return result.Failure == AuthenticationFailure.AccountInactive
            ? Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Account unavailable",
                detail: "The user account is inactive or suspended.")
            : Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed",
                detail: "Invalid username or password.");
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;

        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await _authService.GetCurrentUserAsync(userId.Value, cancellationToken);

        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;

        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await _authService.ChangePasswordAsync(userId.Value, request, cancellationToken);

        if (result.Succeeded)
        {
            return NoContent();
        }

        return result.Error switch
        {
            OperationError.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "User not found",
                detail: result.Message),
            OperationError.Forbidden => Forbid(),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Password change failed",
                detail: result.Message)
        };
    }
}
