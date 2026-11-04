using System.Globalization;
using System.Text;
using Dapper;
using UKK.Models;

namespace UKK.Services;

public sealed class LaporanService
{
    private readonly Database db;

    public LaporanService(Database database)
    {
        db = database;
    }

    public async Task<List<LaporanPeminjamanModel>> GetLaporanPeminjaman(DateOnly from, DateOnly to)
    {
        await using var connection = db.Connect();
        const string sql = """
            SELECT p.id AS IdPeminjaman,
                   p.tanggal_peminjaman AS TanggalPeminjaman,
                   p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                   u.nama AS Peminjam,
                   pt.nama AS NamaPetugas,
                   sp.nama AS Status,
                   COALESCE(SUM(dp.jumlah), 0) AS JumlahAlat,
                   p.total_biaya AS TotalBiaya
            FROM peminjaman p
            INNER JOIN users u ON u.id = p.id_peminjam
            LEFT JOIN users pt ON pt.id = p.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            LEFT JOIN detail_peminjaman dp ON dp.id_peminjaman = p.id
            WHERE p.deleted_at IS NULL
              AND p.tanggal_peminjaman >= @From
              AND p.tanggal_peminjaman <= @To
            GROUP BY p.id,
                     p.tanggal_peminjaman,
                     p.tanggal_rencana_kembali,
                     u.nama,
                     pt.nama,
                     sp.nama,
                     p.total_biaya
            ORDER BY p.tanggal_peminjaman, p.id
            """;
        return (await connection.QueryAsync<LaporanPeminjamanModel>(sql, new
        {
            From = from.ToDateTime(TimeOnly.MinValue),
            To = to.ToDateTime(TimeOnly.MinValue)
        })).ToList();
    }

    public async Task<List<LaporanPengembalianModel>> GetLaporanPengembalian(DateOnly from, DateOnly to)
    {
        await using var connection = db.Connect();
        const string sql = """
            SELECT pr.id AS IdPengembalian,
                   pr.id_peminjaman AS IdPeminjaman,
                   u.nama AS Peminjam,
                   pr.tanggal_kembali AS TanggalKembali,
                   pr.keterlambatan_pengembalian AS KeterlambatanPengembalian,
                   pr.denda AS Denda,
                   COALESCE(SUM(dp.denda_kondisi), 0) AS DendaKondisi
            FROM pengembalian pr
            INNER JOIN peminjaman p ON p.id = pr.id_peminjaman
            INNER JOIN users u ON u.id = p.id_peminjam
            LEFT JOIN detail_pengembalian dp ON dp.id_pengembalian = pr.id
            WHERE pr.deleted_at IS NULL
              AND pr.tanggal_kembali >= @From
              AND pr.tanggal_kembali <= @To
            GROUP BY pr.id,
                     pr.id_peminjaman,
                     u.nama,
                     pr.tanggal_kembali,
                     pr.keterlambatan_pengembalian,
                     pr.denda
            ORDER BY pr.tanggal_kembali, pr.id
            """;
        return (await connection.QueryAsync<LaporanPengembalianModel>(sql, new
        {
            From = from.ToDateTime(TimeOnly.MinValue),
            To = to.ToDateTime(TimeOnly.MinValue)
        })).ToList();
    }

    public static string ToCsv<T>(IEnumerable<T> rows)
    {
        var properties = typeof(T).GetProperties();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', properties.Select(property => Escape(property.Name))));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', properties.Select(property =>
                Escape(Convert.ToString(property.GetValue(row), CultureInfo.InvariantCulture) ?? string.Empty))));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return value.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
