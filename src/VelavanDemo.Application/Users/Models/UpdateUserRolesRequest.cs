using System.ComponentModel.DataAnnotations;

namespace VelavanDemo.Application.Users.Models;

public sealed class UpdateUserRolesRequest
{
    [Required]
    [MinLength(1)]
    public List<string> Roles { get; set; } = new();
}
