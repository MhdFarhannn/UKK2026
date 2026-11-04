using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class KategoriService
{
    private readonly Database db;
    private readonly LogAktivitasService log;

    public KategoriService(Database database, LogAktivitasService activityLog)
    {
        db = database;
        log = activityLog;
    }

    public async Task<List<KategoriModel>> GetAllKategori()
    {
        await using var connection = db.Connect();
        const string sql = """
            SELECT id AS Id, nama AS Nama, deleted_at AS DeletedAt
            FROM kategori
            WHERE deleted_at IS NULL
            ORDER BY nama
            """;
        return (await connection.QueryAsync<KategoriModel>(sql)).ToList();
    }

    public async Task<KategoriModel> GetKategoriById(uint id)
    {
        await using var connection = db.Connect();
        return await FindAsync(connection, null, id, false)
            ?? throw new AppException(404, "Kategori not found");
    }

    public async Task<bool> CreateKategori(KategoriRequest request, uint idUser)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var duplicate = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM kategori WHERE nama = @Nama)",
                new { Nama = name },
                transaction);

            if (duplicate)
            {
                throw new AppException(409, "Kategori already exists");
            }

            var affected = await connection.ExecuteAsync(
                "INSERT INTO kategori (nama) VALUES (@Nama)",
                new { Nama = name },
                transaction);
            await log.Write(connection, transaction, idUser, $"Menambah kategori {name}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdateKategori(uint id, KategoriRequest request, uint idUser)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Kategori not found");
            var duplicate = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM kategori WHERE nama = @Nama AND id <> @Id)",
                new { Nama = name, Id = id },
                transaction);

            if (duplicate)
            {
                throw new AppException(409, "Kategori already exists");
            }

            var affected = await connection.ExecuteAsync(
                "UPDATE kategori SET nama = @Nama WHERE id = @Id AND deleted_at IS NULL",
                new { Nama = name, Id = id },
                transaction);
            await log.Write(connection, transaction, idUser, $"Mengubah kategori {current.Nama}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteKategori(uint id, uint idUser)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Kategori not found");
            var hasActiveAlat = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM alat WHERE id_kategori = @Id AND deleted_at IS NULL)",
                new { Id = id },
                transaction);

            if (hasActiveAlat)
            {
                throw new AppException(409, "Kategori is used by active alat and cannot be deleted");
            }

            var affected = await connection.ExecuteAsync(
                "UPDATE kategori SET deleted_at = CURRENT_TIMESTAMP WHERE id = @Id AND deleted_at IS NULL",
                new { Id = id },
                transaction);
            await log.Write(connection, transaction, idUser, $"Menghapus kategori {current.Nama}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string NormalizeName(string? value)
    {
        return AuthService.NormalizeRequired(value, "nama", 100);
    }

    private static async Task<KategoriModel?> FindAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        uint id,
        bool forUpdate)
    {
        var sql = """
            SELECT id AS Id, nama AS Nama, deleted_at AS DeletedAt
            FROM kategori
            WHERE id = @Id AND deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        return await connection.QuerySingleOrDefaultAsync<KategoriModel>(sql, new { Id = id }, transaction);
    }
}
