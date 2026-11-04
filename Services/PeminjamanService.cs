using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class PeminjamanService
{
    private const decimal MaxMoney = 9_999_999_999_999.99m;

    private readonly Database db;
    private readonly LogAktivitasService log;

    public PeminjamanService(Database database, LogAktivitasService activityLog)
    {
        db = database;
        log = activityLog;
    }

    public async Task<PagedResult<PeminjamanModel>> GetAllPeminjaman(
        int page,
        int pageSize,
        string? status,
        uint? idPeminjam,
        DateOnly? from,
        DateOnly? to)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        await using var connection = db.Connect();
        const string filter = """
            WHERE p.deleted_at IS NULL
              AND (@Status IS NULL OR sp.nama = @Status)
              AND (@IdPeminjam IS NULL OR p.id_peminjam = @IdPeminjam)
              AND (@From IS NULL OR p.tanggal_peminjaman >= @From)
              AND (@To IS NULL OR p.tanggal_peminjaman <= @To)
            """;
        var parameters = new
        {
            Status = Pagination.Search(status),
            IdPeminjam = idPeminjam,
            From = from?.ToDateTime(TimeOnly.MinValue),
            To = to?.ToDateTime(TimeOnly.MinValue),
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM peminjaman p INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman " + filter,
            parameters);
        var items = (await connection.QueryAsync<PeminjamanModel>(
            SelectSql + " " + filter + " ORDER BY p.id DESC LIMIT @Limit OFFSET @Offset",
            parameters)).ToList();
        await LoadDetails(connection, items);

        return new PagedResult<PeminjamanModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public Task<PagedResult<PeminjamanModel>> GetMyPeminjaman(
        uint idPeminjam,
        int page,
        int pageSize,
        string? status)
    {
        return GetAllPeminjaman(page, pageSize, status, idPeminjam, null, null);
    }

    public async Task<PeminjamanModel> GetPeminjamanById(uint id, uint idUser, string role)
    {
        await using var connection = db.Connect();
        var result = await connection.QuerySingleOrDefaultAsync<PeminjamanModel>(SelectSql + " " + """
            WHERE p.id = @Id AND p.deleted_at IS NULL
            LIMIT 1
            """, new { Id = id });

        if (result is null)
        {
            throw new AppException(404, "Peminjaman not found");
        }

        if (role == "Peminjam" && result.IdPeminjam != idUser)
        {
            throw new AppException(403, "You may only view your own peminjaman");
        }

        await LoadDetails(connection, [result]);
        return result;
    }

    public async Task<bool> CreatePeminjaman(PeminjamanRequest request, uint idPeminjam)
    {
        var (start, plannedReturn, rentalDays, items) = ValidateRequest(request);
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var borrowerExists = await connection.QuerySingleOrDefaultAsync<uint?>("""
                SELECT u.id
                FROM users u
                INNER JOIN roles r ON r.id = u.id_role
                WHERE u.id = @Id AND u.deleted_at IS NULL AND r.nama = 'Peminjam'
                FOR UPDATE
                """, new { Id = idPeminjam }, transaction);

            if (borrowerExists is null)
            {
                throw new AppException(403, "Only an active Peminjam can create peminjaman");
            }

            var submittedStatusId = await GetStatusId(connection, transaction, "diajukan");
            const string insertHeader = """
                INSERT INTO peminjaman (
                    id_peminjam,
                    id_status_peminjaman,
                    tanggal_peminjaman,
                    tanggal_rencana_kembali,
                    total_biaya
                )
                VALUES (@IdPeminjam, @IdStatus, @TanggalPeminjaman, @TanggalRencanaKembali, 0)
                """;
            await connection.ExecuteAsync(insertHeader, new
            {
                IdPeminjam = idPeminjam,
                IdStatus = submittedStatusId,
                TanggalPeminjaman = start.ToDateTime(TimeOnly.MinValue),
                TanggalRencanaKembali = plannedReturn.ToDateTime(TimeOnly.MinValue)
            }, transaction);
            var loanId = checked((uint)await connection.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()", transaction: transaction));
            var total = await InsertDetails(connection, transaction, loanId, items, rentalDays);
            EnsureMoney(total, "totalBiaya");
            await connection.ExecuteAsync(
                "UPDATE peminjaman SET total_biaya = @Total WHERE id = @Id",
                new { Total = total, Id = loanId },
                transaction);
            await log.Write(connection, transaction, idPeminjam, $"Mengajukan peminjaman #{loanId}");
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public Task<bool> ApprovePeminjaman(uint id, uint idPetugas)
    {
        return Transition(id, idPetugas, "diajukan", "disetujui", $"Menyetujui peminjaman #{id}");
    }

    public Task<bool> RejectPeminjaman(uint id, uint idPetugas)
    {
        return Transition(id, idPetugas, "diajukan", "ditolak", $"Menolak peminjaman #{id}");
    }

    public async Task<bool> BorrowPeminjaman(uint id, uint idPetugas)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT sp.nama
                FROM peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                WHERE p.id = @Id AND p.deleted_at IS NULL
                FOR UPDATE
                """, new { Id = id }, transaction);

            if (status != "disetujui")
            {
                throw new AppException(409, "Only peminjaman with status 'disetujui' can be handed over");
            }

            var details = (await connection.QueryAsync<DetailRow>("""
                SELECT id AS Id, id_alat AS IdAlat, jumlah AS Jumlah
                FROM detail_peminjaman
                WHERE id_peminjaman = @Id
                ORDER BY id
                """, new { Id = id }, transaction)).ToList();

            if (details.Count == 0)
            {
                throw new AppException(409, "Peminjaman has no detail rows");
            }

            var borrowedStatusId = await GetAvailabilityId(connection, transaction, "dipinjam");

            foreach (var detail in details)
            {
                var units = (await connection.QueryAsync<uint>("""
                    SELECT ua.id
                    FROM unit_alat ua
                    INNER JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
                    INNER JOIN status_barang sb ON sb.id = ua.id_status_barang
                    WHERE ua.id_alat = @IdAlat
                      AND ua.deleted_at IS NULL
                      AND sk.nama = 'tersedia'
                      AND sb.nama <> 'rusak_berat'
                    ORDER BY ua.id
                    LIMIT @Jumlah
                    FOR UPDATE
                    """, new { detail.IdAlat, Jumlah = (long)detail.Jumlah }, transaction)).ToList();

                if ((uint)units.Count < detail.Jumlah)
                {
                    throw new AppException(409, $"Not enough available units for alat #{detail.IdAlat}");
                }

                foreach (var unitId in units)
                {
                    await connection.ExecuteAsync("""
                        INSERT INTO detail_unit_peminjaman (id_detail_peminjaman, id_unit_alat)
                        VALUES (@IdDetail, @IdUnit)
                        """, new { IdDetail = detail.Id, IdUnit = unitId }, transaction);
                    await connection.ExecuteAsync("""
                        UPDATE unit_alat
                        SET id_status_ketersediaan = @StatusId
                        WHERE id = @Id AND deleted_at IS NULL
                        """, new { StatusId = borrowedStatusId, Id = unitId }, transaction);
                }
            }

            var loanStatusId = await GetStatusId(connection, transaction, "dipinjam");
            await connection.ExecuteAsync("""
                UPDATE peminjaman
                SET id_petugas = @IdPetugas,
                    id_status_peminjaman = @IdStatus
                WHERE id = @Id AND deleted_at IS NULL
                """, new { IdPetugas = idPetugas, IdStatus = loanStatusId, Id = id }, transaction);
            await log.Write(connection, transaction, idPetugas, $"Menyerahkan alat peminjaman #{id}");
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdatePeminjaman(uint id, PeminjamanRequest request, uint idAdmin)
    {
        var (start, plannedReturn, rentalDays, items) = ValidateRequest(request);
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT sp.nama
                FROM peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                WHERE p.id = @Id AND p.deleted_at IS NULL
                FOR UPDATE
                """, new { Id = id }, transaction);

            if (status is null)
            {
                throw new AppException(404, "Peminjaman not found");
            }

            if (status != "diajukan")
            {
                throw new AppException(409, "Only peminjaman with status 'diajukan' can be edited");
            }

            await connection.ExecuteAsync("""
                DELETE dup
                FROM detail_unit_peminjaman dup
                INNER JOIN detail_peminjaman dp ON dp.id = dup.id_detail_peminjaman
                WHERE dp.id_peminjaman = @Id
                """, new { Id = id }, transaction);
            await connection.ExecuteAsync(
                "DELETE FROM detail_peminjaman WHERE id_peminjaman = @Id",
                new { Id = id },
                transaction);
            var total = await InsertDetails(connection, transaction, id, items, rentalDays);
            EnsureMoney(total, "totalBiaya");
            await connection.ExecuteAsync("""
                UPDATE peminjaman
                SET tanggal_peminjaman = @TanggalPeminjaman,
                    tanggal_rencana_kembali = @TanggalRencanaKembali,
                    total_biaya = @TotalBiaya
                WHERE id = @Id AND deleted_at IS NULL
                """, new
            {
                Id = id,
                TanggalPeminjaman = start.ToDateTime(TimeOnly.MinValue),
                TanggalRencanaKembali = plannedReturn.ToDateTime(TimeOnly.MinValue),
                TotalBiaya = total
            }, transaction);
            await log.Write(connection, transaction, idAdmin, $"Mengubah peminjaman #{id}");
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeletePeminjaman(uint id, uint idAdmin)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var status = await connection.QuerySingleOrDefaultAsync<string>("""
                SELECT sp.nama
                FROM peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                WHERE p.id = @Id AND p.deleted_at IS NULL
                FOR UPDATE
                """, new { Id = id }, transaction);

            if (status is null)
            {
                throw new AppException(404, "Peminjaman not found");
            }

            if (status is not ("diajukan" or "ditolak"))
            {
                throw new AppException(409, "Only peminjaman with status 'diajukan' or 'ditolak' can be deleted");
            }

            var affected = await connection.ExecuteAsync("""
                UPDATE peminjaman
                SET deleted_at = CURRENT_TIMESTAMP
                WHERE id = @Id AND deleted_at IS NULL
                """, new { Id = id }, transaction);
            await log.Write(connection, transaction, idAdmin, $"Menghapus peminjaman #{id}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<PagedResult<MonitorPeminjamanModel>> GetMonitorPeminjaman(
        int page,
        int pageSize,
        string? status)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        var normalizedStatus = Pagination.Search(status)?.ToLowerInvariant();

        if (normalizedStatus is not (null or "dipinjam" or "jatuh_tempo" or "terlambat"))
        {
            throw new AppException(400, "status must be dipinjam, jatuh_tempo, or terlambat");
        }

        await using var connection = db.Connect();
        const string filter = """
            FROM peminjaman p
            INNER JOIN users u ON u.id = p.id_peminjam
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.deleted_at IS NULL
              AND sp.nama = 'dipinjam'
              AND (@Status IS NULL
                   OR @Status = 'dipinjam'
                   OR (@Status = 'jatuh_tempo' AND p.tanggal_rencana_kembali = CURRENT_DATE)
                   OR (@Status = 'terlambat' AND p.tanggal_rencana_kembali < CURRENT_DATE))
            """;
        var parameters = new
        {
            Status = normalizedStatus,
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };
        var total = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) " + filter, parameters);
        var items = (await connection.QueryAsync<MonitorPeminjamanModel>("""
            SELECT p.id AS Id,
                   u.nama AS NamaPeminjam,
                   u.no_telepon AS NoTelepon,
                   p.tanggal_peminjaman AS TanggalPeminjaman,
                   p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                   GREATEST(0, DATEDIFF(CURRENT_DATE, p.tanggal_rencana_kembali)) AS HariTerlambat,
                   p.total_biaya AS TotalBiaya
            """ + " " + filter + " ORDER BY p.tanggal_rencana_kembali, p.id LIMIT @Limit OFFSET @Offset", parameters)).ToList();

        return new PagedResult<MonitorPeminjamanModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    private async Task<bool> Transition(
        uint id,
        uint idPetugas,
        string fromStatus,
        string toStatus,
        string activity)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var toStatusId = await GetStatusId(connection, transaction, toStatus);
            var affected = await connection.ExecuteAsync("""
                UPDATE peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                SET p.id_petugas = @IdPetugas,
                    p.id_status_peminjaman = @ToStatusId
                WHERE p.id = @Id
                  AND p.deleted_at IS NULL
                  AND sp.nama = @FromStatus
                """, new { Id = id, IdPetugas = idPetugas, ToStatusId = toStatusId, FromStatus = fromStatus }, transaction);

            if (affected == 0)
            {
                throw new AppException(409, $"Only peminjaman with status '{fromStatus}' can transition to '{toStatus}'");
            }

            await log.Write(connection, transaction, idPetugas, activity);
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static (DateOnly Start, DateOnly PlannedReturn, int RentalDays, List<PeminjamanItemRequest> Items) ValidateRequest(
        PeminjamanRequest request)
    {
        if (request.TanggalPeminjaman is null || request.TanggalRencanaKembali is null)
        {
            throw new AppException(400, "tanggalPeminjaman and tanggalRencanaKembali are required");
        }

        var start = request.TanggalPeminjaman.Value;
        var plannedReturn = request.TanggalRencanaKembali.Value;

        if (plannedReturn < start)
        {
            throw new AppException(400, "tanggalRencanaKembali must not be before tanggalPeminjaman");
        }

        if (request.Items is not { Count: > 0 })
        {
            throw new AppException(400, "items must contain at least one alat");
        }

        if (request.Items.Any(item => item is null))
        {
            throw new AppException(400, "items must not contain null entries");
        }

        var items = request.Items.OfType<PeminjamanItemRequest>().ToList();

        if (items.Any(item => item.IdAlat == 0 || item.Jumlah == 0))
        {
            throw new AppException(400, "Every item requires idAlat and jumlah greater than zero");
        }

        if (items.GroupBy(item => item.IdAlat).Any(group => group.Count() > 1))
        {
            throw new AppException(400, "Each alat may only appear once in items");
        }

        return (start, plannedReturn, plannedReturn.DayNumber - start.DayNumber + 1, items);
    }

    private static async Task<decimal> InsertDetails(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint loanId,
        IEnumerable<PeminjamanItemRequest> items,
        int rentalDays)
    {
        decimal total = 0;

        foreach (var item in items)
        {
            var price = await connection.QuerySingleOrDefaultAsync<decimal?>("""
                SELECT harga_sewa
                FROM alat
                WHERE id = @Id AND deleted_at IS NULL
                FOR UPDATE
                """, new { Id = item.IdAlat }, transaction);

            if (price is null)
            {
                throw new AppException(404, $"Alat #{item.IdAlat} not found");
            }

            await connection.ExecuteAsync("""
                INSERT INTO detail_peminjaman (
                    id_peminjaman,
                    id_alat,
                    jumlah,
                    harga_sewa_saat_transaksi
                )
                VALUES (@IdPeminjaman, @IdAlat, @Jumlah, @HargaSewa)
                """, new
            {
                IdPeminjaman = loanId,
                item.IdAlat,
                item.Jumlah,
                HargaSewa = price.Value
            }, transaction);

            checked
            {
                total += price.Value * item.Jumlah * rentalDays;
            }
        }

        return total;
    }

    private static void EnsureMoney(decimal value, string field)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new AppException(400, $"{field} exceeds DECIMAL(15,2) UNSIGNED");
        }
    }

    private static async Task<uint> GetStatusId(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string name)
    {
        var id = await connection.QuerySingleOrDefaultAsync<uint?>(
            "SELECT id FROM status_peminjaman WHERE nama = @Name",
            new { Name = name },
            transaction);
        return id ?? throw new AppException(500, $"Seeded status_peminjaman value '{name}' is missing");
    }

    private static async Task<uint> GetAvailabilityId(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string name)
    {
        var id = await connection.QuerySingleOrDefaultAsync<uint?>(
            "SELECT id FROM status_ketersediaan WHERE nama = @Name",
            new { Name = name },
            transaction);
        return id ?? throw new AppException(500, $"Seeded status_ketersediaan value '{name}' is missing");
    }

    private static async Task LoadDetails(MySqlConnection connection, List<PeminjamanModel> loans)
    {
        if (loans.Count == 0)
        {
            return;
        }

        var rows = await connection.QueryAsync<DetailUnitRow>("""
            SELECT dp.id_peminjaman AS IdPeminjaman,
                   dp.id AS Id,
                   dp.id_alat AS IdAlat,
                   a.nama AS NamaAlat,
                   dp.jumlah AS Jumlah,
                   dp.harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi,
                   dp.created_at AS CreatedAt,
                   ua.id AS IdUnitAlat,
                   ua.kode_inventaris AS KodeInventaris
            FROM detail_peminjaman dp
            INNER JOIN alat a ON a.id = dp.id_alat
            LEFT JOIN detail_unit_peminjaman dup ON dup.id_detail_peminjaman = dp.id
            LEFT JOIN unit_alat ua ON ua.id = dup.id_unit_alat
            WHERE dp.id_peminjaman IN @Ids
            ORDER BY dp.id, ua.kode_inventaris
            """, new { Ids = loans.Select(loan => loan.Id).ToArray() });
        var loansById = loans.ToDictionary(loan => loan.Id);

        foreach (var row in rows)
        {
            if (!loansById.TryGetValue(row.IdPeminjaman, out var loan))
            {
                continue;
            }

            var detail = loan.Details.FirstOrDefault(item => item.Id == row.Id);

            if (detail is null)
            {
                detail = new DetailPeminjamanModel
                {
                    Id = row.Id,
                    IdPeminjaman = row.IdPeminjaman,
                    IdAlat = row.IdAlat,
                    NamaAlat = row.NamaAlat,
                    Jumlah = row.Jumlah,
                    HargaSewaSaatTransaksi = row.HargaSewaSaatTransaksi,
                    CreatedAt = row.CreatedAt
                };
                loan.Details.Add(detail);
            }

            if (row.IdUnitAlat is not null && row.KodeInventaris is not null)
            {
                detail.IdUnitAlat.Add(row.IdUnitAlat.Value);
                detail.KodeInventaris.Add(row.KodeInventaris);
            }
        }
    }

    private const string SelectSql = """
        SELECT p.id AS Id,
               p.id_peminjam AS IdPeminjam,
               u.username AS UsernamePeminjam,
               u.nama AS NamaPeminjam,
               p.id_petugas AS IdPetugas,
               pt.nama AS NamaPetugas,
               p.id_status_peminjaman AS IdStatusPeminjaman,
               sp.nama AS Status,
               p.tanggal_peminjaman AS TanggalPeminjaman,
               p.tanggal_rencana_kembali AS TanggalRencanaKembali,
               p.total_biaya AS TotalBiaya,
               p.created_at AS CreatedAt,
               p.deleted_at AS DeletedAt
        FROM peminjaman p
        INNER JOIN users u ON u.id = p.id_peminjam
        LEFT JOIN users pt ON pt.id = p.id_petugas
        INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
        """;

    private sealed class DetailRow
    {
        public uint Id { get; init; }
        public uint IdAlat { get; init; }
        public uint Jumlah { get; init; }
    }

    private sealed class DetailUnitRow
    {
        public uint IdPeminjaman { get; init; }
        public uint Id { get; init; }
        public uint IdAlat { get; init; }
        public string NamaAlat { get; init; } = string.Empty;
        public uint Jumlah { get; init; }
        public decimal HargaSewaSaatTransaksi { get; init; }
        public DateTime CreatedAt { get; init; }
        public uint? IdUnitAlat { get; init; }
        public string? KodeInventaris { get; init; }
    }
}
