using System.ComponentModel.DataAnnotations;

namespace UKK.Models;

public sealed class AppException : Exception
{
    public AppException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
}

public sealed class LoginRequest
{
    [Required, MaxLength(50)]
    public string Username { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required, MaxLength(4096)]
    public string CaptchaToken { get; init; } = string.Empty;
}

public sealed class CaptchaConfigResponse
{
    public string SiteKey { get; init; } = string.Empty;
    public string Version { get; init; } = "v2";
}

public sealed class RecaptchaOptions
{
    public const string GoogleTestSiteKey = "6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI";
    public const string GoogleTestSecretKey = "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe";

    public string SiteKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string VerificationEndpoint { get; init; } = "https://www.google.com/recaptcha/api/siteverify";
    public string[] AllowedHostnames { get; init; } = [];
}

public sealed class RegisterRequest
{
    public string Username { get; init; } = string.Empty;
    public string Nama { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string NoTelepon { get; init; } = string.Empty;
    public string? Alamat { get; init; }
}

public sealed class AuthResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public UserResponse User { get; init; } = new();
}

public sealed class UserResponse
{
    public uint Id { get; init; }
    public uint IdRole { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Nama { get; init; } = string.Empty;
    public string NoTelepon { get; init; } = string.Empty;
    public string? Alamat { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class UserRequest
{
    public uint IdRole { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Nama { get; init; } = string.Empty;
    public string? Password { get; init; }
    public string NoTelepon { get; init; } = string.Empty;
    public string? Alamat { get; init; }
}

public sealed class KategoriRequest
{
    public string Nama { get; init; } = string.Empty;
}

public sealed class AlatRequest
{
    public uint IdKategori { get; init; }
    public string Nama { get; init; } = string.Empty;
    public string? Deskripsi { get; init; }
    public decimal HargaSewa { get; init; }
    public string? ImagePath { get; init; }
}

public sealed class AlatFormRequest
{
    public uint IdKategori { get; init; }
    public string Nama { get; init; } = string.Empty;
    public string? Deskripsi { get; init; }
    public decimal HargaSewa { get; init; }
    public string? ImagePath { get; init; }
    public IFormFile? Gambar { get; init; }
}

public sealed class UnitAlatRequest
{
    public uint IdAlat { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
    public uint IdStatusBarang { get; init; }
}

public sealed class UnitAlatUpdateRequest
{
    public string KodeInventaris { get; init; } = string.Empty;
    public uint IdStatusBarang { get; init; }
    public uint IdStatusKetersediaan { get; init; }
}

public sealed class StatusBarangModel
{
    public uint Id { get; init; }
    public string Nama { get; init; } = string.Empty;
}

public sealed class StatusBarangRequest
{
    public string Nama { get; init; } = string.Empty;
}

public sealed class PeminjamanRequest
{
    public DateOnly? TanggalPeminjaman { get; init; }
    public DateOnly? TanggalRencanaKembali { get; init; }
    public List<PeminjamanItemRequest?>? Items { get; init; } = [];
}

public sealed class PeminjamanItemRequest
{
    public uint IdAlat { get; init; }
    public uint Jumlah { get; init; }
}

public sealed class MonitorPeminjamanModel
{
    public uint Id { get; init; }
    public string NamaPeminjam { get; init; } = string.Empty;
    public string NoTelepon { get; init; } = string.Empty;
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public uint HariTerlambat { get; init; }
    public decimal TotalBiaya { get; init; }
}

public sealed class PengembalianRequest
{
    public uint IdPeminjaman { get; init; }
    public DateOnly? TanggalKembali { get; init; }
    public string? Catatan { get; init; }
    public List<PengembalianUnitRequest?>? Units { get; init; } = [];
}

public sealed class PengembalianUnitRequest
{
    public uint IdUnitAlat { get; init; }
    public uint IdStatusKondisiKembali { get; init; }
    public string? Catatan { get; init; }
}

public sealed class DendaSettings
{
    public decimal TerlambatPerHari { get; init; }
    public decimal RusakRingan { get; init; }
    public decimal RusakBerat { get; init; }
    public decimal Hilang { get; init; }
}
