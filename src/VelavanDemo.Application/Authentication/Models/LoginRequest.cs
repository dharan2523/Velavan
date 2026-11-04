using System.ComponentModel.DataAnnotations;

namespace VelavanDemo.Application.Authentication.Models;

public sealed class LoginRequest
{
    [Required]
    [MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;
}
