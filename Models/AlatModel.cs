namespace UKK.Models;

public sealed class AlatModel
{
    public uint Id { get; init; }
    public uint IdKategori { get; init; }
    public string Kategori { get; init; } = string.Empty;
    public string Nama { get; init; } = string.Empty;
    public string? Deskripsi { get; init; }
    public decimal HargaSewa { get; init; }
    public string? ImagePath { get; init; }
    public long StokTersedia { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
