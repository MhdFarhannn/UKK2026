namespace UKK.Models;

public sealed class LogAktivitasModel
{
    public uint Id { get; init; }
    public uint? IdUser { get; init; }
    public string? Username { get; init; }
    public string? NamaUser { get; init; }
    public string Aktivitas { get; init; } = string.Empty;
    public DateTime Waktu { get; init; }
}
