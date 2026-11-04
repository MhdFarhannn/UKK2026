using Dapper;
using UKK.Models;

namespace UKK.Services;

public sealed class StatusBarangServices
{
    private static readonly HashSet<string> SeededNames =
        new(StringComparer.Ordinal) { "baik", "rusak_ringan", "rusak_berat" };

    private readonly Database db;

    public StatusBarangServices(Database database)
    {
        db = database;
    }

    public async Task<bool> CreateStatusBarang(StatusBarangRequest request)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = db.Connect();
        var affected = await connection.ExecuteAsync(
            "INSERT INTO status_barang (nama) VALUES (@Nama)",
            new { Nama = name });
        return affected == 1;
    }

    public async Task<bool> DeleteStatusBarang(uint id)
    {
        await using var connection = db.Connect();
        var current = await GetById(connection, id);

        if (SeededNames.Contains(current.Nama))
        {
            throw new AppException(409, "Seeded status_barang values cannot be deleted");
        }

        var affected = await connection.ExecuteAsync(
            "DELETE FROM status_barang WHERE id = @Id",
            new { Id = id });
        return affected == 1;
    }

    public async Task<bool> PatchStatusBarang(uint id, StatusBarangRequest request)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = db.Connect();
        var current = await GetById(connection, id);

        if (SeededNames.Contains(current.Nama) && current.Nama != name)
        {
            throw new AppException(409, "Seeded status_barang values cannot be renamed");
        }

        var affected = await connection.ExecuteAsync(
            "UPDATE status_barang SET nama = @Nama WHERE id = @Id",
            new { Id = id, Nama = name });
        return affected == 1;
    }

    public async Task<List<StatusBarangModel>> GetAllStatusBarang()
    {
        await using var connection = db.Connect();
        var result = await connection.QueryAsync<StatusBarangModel>(
            "SELECT id AS Id, nama AS Nama FROM status_barang ORDER BY id");
        return result.ToList();
    }

    private static string NormalizeName(string? value)
    {
        return AuthService.NormalizeRequired(value, "nama", 30);
    }

    private static async Task<StatusBarangModel> GetById(System.Data.IDbConnection connection, uint id)
    {
        return await connection.QuerySingleOrDefaultAsync<StatusBarangModel>(
            "SELECT id AS Id, nama AS Nama FROM status_barang WHERE id = @Id",
            new { Id = id })
            ?? throw new AppException(404, "Status barang not found");
    }
}
