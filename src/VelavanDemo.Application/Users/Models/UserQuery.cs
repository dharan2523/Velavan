using System.ComponentModel.DataAnnotations;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Users.Models;

public sealed class UserQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [MaxLength(100)]
    public string? Search { get; set; }

    public UserStatus? Status { get; set; }

    [MaxLength(50)]
    public string? Role { get; set; }
}
