using System.ComponentModel.DataAnnotations;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Users.Models;

public sealed class UpdateUserStatusRequest
{
    [Required]
    [EnumDataType(typeof(UserStatus))]
    public UserStatus Status { get; set; }
}
