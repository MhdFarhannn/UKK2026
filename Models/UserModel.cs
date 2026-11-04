namespace UKK.Models;

public sealed class UserModel
{
    public uint Id { get; init; }
    public uint IdRole { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Nama { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string NoTelepon { get; init; } = string.Empty;
    public string? Alamat { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
