using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class AlatService
{
    private const decimal MaxMoney = 9_999_999_999_999.99m;

    private readonly Database db;
    private readonly LogAktivitasService log;
    private readonly ImageService images;
    private readonly ILogger<AlatService> logger;

    public AlatService(
        Database database,
        LogAktivitasService activityLog,
        ImageService imageService,
        ILogger<AlatService> serviceLogger)
    {
        db = database;
        log = activityLog;
        images = imageService;
        logger = serviceLogger;
    }

    public async Task<PagedResult<AlatModel>> GetAllAlat(
        int page,
        int pageSize,
        string? search,
        uint? idKategori)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        await using var connection = db.Connect();

        const string filter = """
            WHERE a.deleted_at IS NULL
              AND k.deleted_at IS NULL
              AND (@Search IS NULL OR a.nama LIKE CONCAT('%', @Search, '%'))
              AND (@IdKategori IS NULL OR a.id_kategori = @IdKategori)
            """;
        var parameters = new
        {
            Search = Pagination.Search(search),
            IdKategori = idKategori,
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };

        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM alat a INNER JOIN kategori k ON k.id = a.id_kategori " + filter,
            parameters);
        var items = (await connection.QueryAsync<AlatModel>(SelectSql + " " + filter + " ORDER BY a.nama LIMIT @Limit OFFSET @Offset", parameters)).ToList();

        return new PagedResult<AlatModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<AlatModel> GetAlatById(uint id)
    {
        await using var connection = db.Connect();
        return await FindAsync(connection, null, id, false)
            ?? throw new AppException(404, "Alat not found");
    }

    public async Task<bool> CreateAlat(AlatRequest request, uint idUser, IFormFile? gambar)
    {
        var normalized = Normalize(request);
        string? uploaded = null;

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await EnsureCategoryExists(connection, transaction, normalized.IdKategori);
            uploaded = await images.SaveAlatImage(gambar);
            var imagePath = uploaded ?? normalized.ImagePath;
            const string sql = """
                INSERT INTO alat (id_kategori, nama, deskripsi, harga_sewa, image_path)
                VALUES (@IdKategori, @Nama, @Deskripsi, @HargaSewa, @ImagePath)
                """;
            var affected = await connection.ExecuteAsync(sql, new
            {
                normalized.IdKategori,
                normalized.Nama,
                normalized.Deskripsi,
                normalized.HargaSewa,
                ImagePath = imagePath
            }, transaction);
            await log.Write(connection, transaction, idUser, $"Menambah alat {normalized.Nama}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();

            if (uploaded is not null)
            {
                TryDeleteImage(uploaded);
            }

            throw;
        }
    }

    public async Task<bool> UpdateAlat(uint id, AlatRequest request, uint idUser, IFormFile? gambar)
    {
        var normalized = Normalize(request);
        string? uploaded = null;
        string? oldImagePath = null;
        var affected = 0;

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Alat not found");
            oldImagePath = current.ImagePath;
            await EnsureCategoryExists(connection, transaction, normalized.IdKategori);

            uploaded = await images.SaveAlatImage(gambar);
            var imagePath = uploaded ?? normalized.ImagePath ?? current.ImagePath;
            const string sql = """
                UPDATE alat
                SET id_kategori = @IdKategori,
                    nama = @Nama,
                    deskripsi = @Deskripsi,
                    harga_sewa = @HargaSewa,
                    image_path = @ImagePath
                WHERE id = @Id AND deleted_at IS NULL
                """;
            affected = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                normalized.IdKategori,
                normalized.Nama,
                normalized.Deskripsi,
                normalized.HargaSewa,
                ImagePath = imagePath
            }, transaction);
            await log.Write(connection, transaction, idUser, $"Mengubah alat {current.Nama}");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();

            if (uploaded is not null)
            {
                TryDeleteImage(uploaded);
            }

            throw;
        }

        if (uploaded is not null && oldImagePath != uploaded)
        {
            TryDeleteImage(oldImagePath);
        }

        return affected == 1;
    }

    public async Task<bool> DeleteAlat(uint id, uint idUser)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "Alat not found");
            var hasActiveUnits = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM unit_alat WHERE id_alat = @Id AND deleted_at IS NULL)",
                new { Id = id },
                transaction);

            if (hasActiveUnits)
            {
                throw new AppException(409, "Alat still has active units and cannot be deleted");
            }

            var affected = await connection.ExecuteAsync(
                "UPDATE alat SET deleted_at = CURRENT_TIMESTAMP WHERE id = @Id AND deleted_at IS NULL",
                new { Id = id },
                transaction);
            await log.Write(connection, transaction, idUser, $"Menghapus alat {current.Nama}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static AlatRequest Normalize(AlatRequest request)
    {
        if (request.IdKategori == 0)
        {
            throw new AppException(400, "idKategori is required");
        }

        var name = AuthService.NormalizeRequired(request.Nama, "nama", 150);

        if (request.HargaSewa < 0 || request.HargaSewa > MaxMoney || decimal.Round(request.HargaSewa, 2) != request.HargaSewa)
        {
            throw new AppException(400, "hargaSewa must fit DECIMAL(15,2) UNSIGNED");
        }

        var imagePath = AuthService.NormalizeOptional(request.ImagePath);

        if (imagePath?.Length > 255)
        {
            throw new AppException(400, "imagePath must not exceed 255 characters");
        }

        return new AlatRequest
        {
            IdKategori = request.IdKategori,
            Nama = name,
            Deskripsi = AuthService.NormalizeOptional(request.Deskripsi),
            HargaSewa = request.HargaSewa,
            ImagePath = imagePath
        };
    }

    private void TryDeleteImage(string? imagePath)
    {
        try
        {
            images.Delete(imagePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete alat image {ImagePath}", imagePath);
        }
    }

    private static Task EnsureCategoryExists(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint idKategori)
    {
        return EnsureExists();

        async Task EnsureExists()
        {
            var exists = await connection.QuerySingleOrDefaultAsync<uint?>(
                "SELECT id FROM kategori WHERE id = @Id AND deleted_at IS NULL FOR UPDATE",
                new { Id = idKategori },
                transaction);

            if (exists is null)
            {
                throw new AppException(404, "Kategori not found");
            }
        }
    }

    private static async Task<AlatModel?> FindAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        uint id,
        bool forUpdate)
    {
        var sql = SelectSql + " " + """
            WHERE a.id = @Id
              AND a.deleted_at IS NULL
              AND k.deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);
        return await connection.QuerySingleOrDefaultAsync<AlatModel>(sql, new { Id = id }, transaction);
    }

    private const string SelectSql = """
        SELECT a.id AS Id,
               a.id_kategori AS IdKategori,
               k.nama AS Kategori,
               a.nama AS Nama,
               a.deskripsi AS Deskripsi,
               a.harga_sewa AS HargaSewa,
               a.image_path AS ImagePath,
               COALESCE(s.stok, 0) AS StokTersedia,
               a.created_at AS CreatedAt,
               a.deleted_at AS DeletedAt
        FROM alat a
        INNER JOIN kategori k ON k.id = a.id_kategori
        LEFT JOIN (
            SELECT ua.id_alat, COUNT(*) AS stok
            FROM unit_alat ua
            INNER JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            INNER JOIN status_barang sb ON sb.id = ua.id_status_barang
            WHERE ua.deleted_at IS NULL
              AND sk.nama = 'tersedia'
              AND sb.nama <> 'rusak_berat'
            GROUP BY ua.id_alat
        ) s ON s.id_alat = a.id
        """;
}
