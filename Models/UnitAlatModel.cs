namespace UKK.Models;

public sealed class UnitAlatModel
{
    public uint Id { get; init; }
    public uint IdAlat { get; init; }
    public string NamaAlat { get; init; } = string.Empty;
    public uint IdStatusKetersediaan { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
    public uint IdStatusBarang { get; init; }
    public string StatusKetersediaan { get; init; } = string.Empty;
    public string StatusBarang { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
