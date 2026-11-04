namespace UKK.Models;

public sealed class LaporanPeminjamanModel
{
    public uint IdPeminjaman { get; init; }
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public string Peminjam { get; init; } = string.Empty;
    public string? NamaPetugas { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal JumlahAlat { get; init; }
    public decimal TotalBiaya { get; init; }
}

public sealed class LaporanPengembalianModel
{
    public uint IdPengembalian { get; init; }
    public uint IdPeminjaman { get; init; }
    public string Peminjam { get; init; } = string.Empty;
    public DateOnly TanggalKembali { get; init; }
    public uint KeterlambatanPengembalian { get; init; }
    public decimal Denda { get; init; }
    public decimal DendaKondisi { get; init; }
    public decimal TotalDenda => Denda + DendaKondisi;
}
