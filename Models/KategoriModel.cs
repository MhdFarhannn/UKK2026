namespace UKK.Models;

public sealed class KategoriModel
{
    public uint Id { get; init; }
    public string Nama { get; init; } = string.Empty;
    public DateTime? DeletedAt { get; init; }
}
