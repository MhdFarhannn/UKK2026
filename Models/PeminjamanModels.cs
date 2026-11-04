namespace UKK.Models;

public sealed class PeminjamanModel
{
    public uint Id { get; init; }
    public uint IdPeminjam { get; init; }
    public string UsernamePeminjam { get; init; } = string.Empty;
    public string NamaPeminjam { get; init; } = string.Empty;
    public uint? IdPetugas { get; init; }
    public string? NamaPetugas { get; init; }
    public uint IdStatusPeminjaman { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public decimal TotalBiaya { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public List<DetailPeminjamanModel> Details { get; init; } = [];
}

public sealed class DetailPeminjamanModel
{
    public uint Id { get; init; }
    public uint IdPeminjaman { get; init; }
    public uint IdAlat { get; init; }
    public string NamaAlat { get; init; } = string.Empty;
    public uint Jumlah { get; init; }
    public decimal HargaSewaSaatTransaksi { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<uint> IdUnitAlat { get; init; } = [];
    public List<string> KodeInventaris { get; init; } = [];
}

public sealed class UnitPeminjamanModel
{
    public uint Id { get; init; }
    public uint IdDetailPeminjaman { get; init; }
    public uint IdUnitAlat { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
}

public sealed class KetersediaanAlatModel
{
    public uint Id { get; init; }
    public string Nama { get; init; } = string.Empty;
    public decimal HargaSewa { get; init; }
    public long StokTersedia { get; init; }
}
