using System.ComponentModel.DataAnnotations;

namespace VelavanDemo.Application.Users.Models;

public sealed class UpdateUserRequest
{
    [Required]
    [MaxLength(256)]
    public string Username { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; set; }

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }
}
