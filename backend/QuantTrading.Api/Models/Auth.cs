using System.ComponentModel.DataAnnotations;

namespace QuantTrading.Api.Models;

public class AppUser
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(80)]
    public string Username { get; set; } = string.Empty;

    [StringLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Role { get; set; } = UserRoles.User;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }
}

public static class UserRoles
{
    public const string Admin = "admin";
    public const string User = "user";
}
