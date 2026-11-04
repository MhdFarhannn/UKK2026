using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class UnitAlatService
{
    private readonly Database db;
    private readonly LogAktivitasService log;

    public UnitAlatService(Database database, LogAktivitasService activityLog)
    {
        db = database;
        log = activityLog;
    }

    public async Task<PagedResult<UnitAlatModel>> GetAllUnitAlat(
        int page,
        int pageSize,
        uint? idAlat,
        uint? idStatusKetersediaan)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        await using var connection = db.Connect();
        const string filter = """
            WHERE ua.deleted_at IS NULL
              AND a.deleted_at IS NULL
              AND (@IdAlat IS NULL OR ua.id_alat = @IdAlat)
              AND (@IdStatusKetersediaan IS NULL OR ua.id_status_ketersediaan = @IdStatusKetersediaan)
            """;
        var parameters = new
        {
            IdAlat = idAlat,
            IdStatusKetersediaan = idStatusKetersediaan,
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM unit_alat ua INNER JOIN alat a ON a.id = ua.id_alat " + filter,
            parameters);
        var items = (await connection.QueryAsync<UnitAlatModel>(
            SelectSql + " " + filter + " ORDER BY ua.kode_inventaris LIMIT @Limit OFFSET @Offset",
            parameters)).ToList();

        return new PagedResult<UnitAlatModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<UnitAlatModel> GetUnitAlatById(uint id)
    {
        await using var connection = db.Connect();
        return await FindAsync(connection, null, id, false)
            ?? throw new AppException(404, "Unit alat not found");
    }

    public async Task<bool> CreateUnitAlat(UnitAlatRequest request, uint idUser)
    {
        var code = NormalizeCode(request.KodeInventaris);

        if (request.IdAlat == 0 || request.IdStatusBarang == 0)
        {
            throw new AppException(400, "idAlat and idStatusBarang are required");
        }

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var alatExists = await connection.QuerySingleOrDefaultAsync<uint?>(
                "SELECT id FROM alat WHERE id = @Id AND deleted_at IS NULL FOR UPDATE",
                new { Id = request.IdAlat },
                transaction);

            if (alatExists is null)
            {
                throw new AppException(404, "Alat not found");
            }

            await EnsureUniqueCode(connection, transaction, code, null);
            var statusBarang = await GetLookupName(connection, transaction, "status_barang", request.IdStatusBarang);
            var availabilityName = statusBarang == "rusak_berat" ? "tidak_tersedia" : "tersedia";
            var availabilityId = await GetLookupId(connection, transaction, "status_ketersediaan", availabilityName);

            const string sql = """
                INSERT INTO unit_alat (id_alat, id_status_ketersediaan, kode_inventaris, id_status_barang)
                VALUES (@IdAlat, @IdStatusKetersediaan, @KodeInventaris, @IdStatusBarang)
                """;
            var affected = await connection.ExecuteAsync(sql, new
            {
                request.IdAlat,
                IdStatusKetersediaan = availabilityId,
                KodeInventaris = code,
                request.IdStatusBarang
            }, transaction);
            await log.Write(connection, transaction, idUser, $"Menambah unit alat {code}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdateUnitAlat(uint id, UnitAlatUpdateRequest request, uint idUser)
    {
        var code = NormalizeCode(request.KodeInventaris);

        if (request.IdStatusBarang == 0 || request.IdStatusKetersediaan == 0)
        {
            throw new AppException(400, "idStatusBarang and idStatusKetersediaan are required");
        }

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Unit alat not found");

            if (current.StatusKetersediaan == "dipinjam")
            {
                throw new AppException(409, "Unit alat is currently borrowed and cannot be changed");
            }

            await EnsureUniqueCode(connection, transaction, code, id);
            var conditionName = await GetLookupName(connection, transaction, "status_barang", request.IdStatusBarang);
            var availabilityName = await GetLookupName(connection, transaction, "status_ketersediaan", request.IdStatusKetersediaan);

            if (availabilityName == "dipinjam")
            {
                throw new AppException(400, "The dipinjam status is managed by the peminjaman workflow");
            }

            if (conditionName == "rusak_berat" && availabilityName != "tidak_tersedia")
            {
                throw new AppException(400, "A rusak_berat unit must be tidak_tersedia");
            }

            const string sql = """
                UPDATE unit_alat
                SET kode_inventaris = @KodeInventaris,
                    id_status_barang = @IdStatusBarang,
                    id_status_ketersediaan = @IdStatusKetersediaan
                WHERE id = @Id AND deleted_at IS NULL
                """;
            var affected = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                KodeInventaris = code,
                request.IdStatusBarang,
                request.IdStatusKetersediaan
            }, transaction);
            await log.Write(connection, transaction, idUser, $"Mengubah unit alat {current.KodeInventaris}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteUnitAlat(uint id, uint idUser)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Unit alat not found");

            if (current.StatusKetersediaan == "dipinjam")
            {
                throw new AppException(409, "A borrowed unit cannot be deleted");
            }

            var affected = await connection.ExecuteAsync(
                "UPDATE unit_alat SET deleted_at = CURRENT_TIMESTAMP WHERE id = @Id AND deleted_at IS NULL",
                new { Id = id },
                transaction);
            await log.Write(connection, transaction, idUser, $"Menghapus unit alat {current.KodeInventaris}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string NormalizeCode(string? value)
    {
        return AuthService.NormalizeRequired(value, "kodeInventaris", 50);
    }

    private static async Task EnsureUniqueCode(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string code,
        uint? excludedId)
    {
        var exists = await connection.ExecuteScalarAsync<bool>("""
            SELECT EXISTS(
                SELECT 1 FROM unit_alat
                WHERE kode_inventaris = @Code
                  AND (@ExcludedId IS NULL OR id <> @ExcludedId)
            )
            """, new { Code = code, ExcludedId = excludedId }, transaction);

        if (exists)
        {
            throw new AppException(409, "kodeInventaris is already used");
        }
    }

    private static async Task<string> GetLookupName(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string table,
        uint id)
    {
        var sql = table switch
        {
            "status_barang" => "SELECT nama FROM status_barang WHERE id = @Id",
            "status_ketersediaan" => "SELECT nama FROM status_ketersediaan WHERE id = @Id",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        var name = await connection.QuerySingleOrDefaultAsync<string>(sql, new { Id = id }, transaction);
        return name ?? throw new AppException(400, $"id for {table} does not exist");
    }

    private static async Task<uint> GetLookupId(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string table,
        string name)
    {
        var sql = table switch
        {
            "status_ketersediaan" => "SELECT id FROM status_ketersediaan WHERE nama = @Name",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        var id = await connection.QuerySingleOrDefaultAsync<uint?>(sql, new { Name = name }, transaction);
        return id ?? throw new AppException(500, $"Seeded {table} value '{name}' is missing");
    }

    private static async Task<UnitAlatModel?> FindAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        uint id,
        bool forUpdate)
    {
        var sql = SelectSql + " " + """
            WHERE ua.id = @Id
              AND ua.deleted_at IS NULL
              AND a.deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        return await connection.QuerySingleOrDefaultAsync<UnitAlatModel>(sql, new { Id = id }, transaction);
    }

    private const string SelectSql = """
        SELECT ua.id AS Id,
               ua.id_alat AS IdAlat,
               a.nama AS NamaAlat,
               ua.id_status_ketersediaan AS IdStatusKetersediaan,
               ua.kode_inventaris AS KodeInventaris,
               ua.id_status_barang AS IdStatusBarang,
               sk.nama AS StatusKetersediaan,
               sb.nama AS StatusBarang,
               ua.created_at AS CreatedAt,
               ua.deleted_at AS DeletedAt
        FROM unit_alat ua
        INNER JOIN alat a ON a.id = ua.id_alat
        INNER JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
        INNER JOIN status_barang sb ON sb.id = ua.id_status_barang
        """;
}
