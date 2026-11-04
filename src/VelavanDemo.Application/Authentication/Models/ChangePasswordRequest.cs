using System.ComponentModel.DataAnnotations;
using VelavanDemo.Application.Validation;

namespace VelavanDemo.Application.Authentication.Models;

public sealed class ChangePasswordRequest
{
    [Required]
    [MaxLength(200)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [StrongPassword]
    public string NewPassword { get; set; } = string.Empty;
}
