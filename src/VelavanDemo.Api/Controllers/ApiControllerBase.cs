using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Application.Security;
using VelavanDemo.Domain.Authorization;

namespace VelavanDemo.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected int? CurrentUserId
    {
        get
        {
            var value = User.FindFirstValue(AuthClaimTypes.UserId);
            return int.TryParse(value, out var userId) ? userId : null;
        }
    }

    protected bool IsSuperAdmin => User.IsInRole(RoleNames.SuperAdmin);
}
