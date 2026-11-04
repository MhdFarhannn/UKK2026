using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class PengembalianService
{
    private const decimal MaxMoney = 9_999_999_999_999.99m;

    private readonly Database db;
    private readonly LogAktivitasService log;
    private readonly DendaSettings denda;

    public PengembalianService(
        Database database,
        LogAktivitasService activityLog,
        IOptions<DendaSettings> fineOptions)
    {
        db = database;
        log = activityLog;
        denda = fineOptions.Value;
        ValidateFine(denda.TerlambatPerHari, "Denda:TerlambatPerHari");
        ValidateFine(denda.RusakRingan, "Denda:RusakRingan");
        ValidateFine(denda.RusakBerat, "Denda:RusakBerat");
        ValidateFine(denda.Hilang, "Denda:Hilang");
    }

    public async Task<PagedResult<PengembalianModel>> GetAllPengembalian(
        int page,
        int pageSize,
        DateOnly? from,
        DateOnly? to)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        await using var connection = db.Connect();
        const string filter = """
            WHERE pr.deleted_at IS NULL
              AND (@From IS NULL OR pr.tanggal_kembali >= @From)
              AND (@To IS NULL OR pr.tanggal_kembali <= @To)
            """;
        var parameters = new
        {
            From = from?.ToDateTime(TimeOnly.MinValue),
            To = to?.ToDateTime(TimeOnly.MinValue),
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };
        var total = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM pengembalian pr " + filter,
            parameters);
        var items = (await connection.QueryAsync<PengembalianModel>(
            SelectSql + " " + filter + " ORDER BY pr.id DESC LIMIT @Limit OFFSET @Offset",
            parameters)).ToList();
        await LoadDetails(connection, items);

        return new PagedResult<PengembalianModel>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<PengembalianModel> GetPengembalianById(uint id, uint idUser, string role)
    {
        await using var connection = db.Connect();
        var result = await connection.QuerySingleOrDefaultAsync<PengembalianModel>(SelectSql + " " + """
            WHERE pr.id = @Id AND pr.deleted_at IS NULL
            LIMIT 1
            """, new { Id = id });

        if (result is null)
        {
            throw new AppException(404, "Pengembalian not found");
        }

        if (role == "Peminjam" && result.IdPeminjam != idUser)
        {
            throw new AppException(403, "You may only view your own pengembalian");
        }

        await LoadDetails(connection, [result]);
        return result;
    }

    public async Task<bool> CreatePengembalian(PengembalianRequest request, uint idUser, string role)
    {
        if (role is not ("Admin" or "Petugas"))
        {
            throw new AppException(403, "Only Admin or Petugas can process pengembalian");
        }

        var units = ValidateRequest(request);
        var returnDate = request.TanggalKembali!.Value;
        var requestedUnits = units.ToDictionary(unit => unit.IdUnitAlat);

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var loan = await connection.QuerySingleOrDefaultAsync<LoanRow>("""
                SELECT p.id_peminjam AS IdPeminjam,
                       p.tanggal_peminjaman AS TanggalPeminjaman,
                       p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                       sp.nama AS Status
                FROM peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                WHERE p.id = @Id AND p.deleted_at IS NULL
                FOR UPDATE
                """, new { Id = request.IdPeminjaman }, transaction);

            if (loan is null)
            {
                throw new AppException(404, "Peminjaman not found");
            }

            if (role == "Peminjam" && loan.IdPeminjam != idUser)
            {
                throw new AppException(403, "You may only return your own peminjaman");
            }

            if (loan.Status != "dipinjam")
            {
                throw new AppException(409, "Only peminjaman with status 'dipinjam' can be returned");
            }

            if (returnDate < loan.TanggalPeminjaman)
            {
                throw new AppException(400, "tanggalKembali must not be before tanggalPeminjaman");
            }

            var existingReturn = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM pengembalian WHERE id_peminjaman = @Id)",
                new { Id = request.IdPeminjaman },
                transaction);

            if (existingReturn)
            {
                throw new AppException(409, "This peminjaman has already been returned");
            }

            var loanUnits = (await connection.QueryAsync<uint>("""
                SELECT ua.id
                FROM detail_unit_peminjaman dup
                INNER JOIN detail_peminjaman dp ON dp.id = dup.id_detail_peminjaman
                INNER JOIN unit_alat ua ON ua.id = dup.id_unit_alat
                WHERE dp.id_peminjaman = @Id
                  AND ua.deleted_at IS NULL
                ORDER BY ua.id
                FOR UPDATE
                """, new { Id = request.IdPeminjaman }, transaction)).ToHashSet();

            if (loanUnits.Count == 0)
            {
                throw new AppException(409, "Peminjaman has no assigned units");
            }

            if (loanUnits.Count != requestedUnits.Count || requestedUnits.Keys.Any(id => !loanUnits.Contains(id)))
            {
                throw new AppException(400, "units must contain every unit assigned to this peminjaman exactly once");
            }

            var lateDays = (uint)Math.Max(0, returnDate.DayNumber - loan.TanggalRencanaKembali.DayNumber);
            var lateFine = denda.TerlambatPerHari * lateDays;
            ValidateFine(lateFine, "denda");
            var staffId = idUser;
            const string insertHeader = """
                INSERT INTO pengembalian (
                    id_peminjaman,
                    id_petugas,
                    tanggal_kembali,
                    keterlambatan_pengembalian,
                    denda,
                    catatan
                )
                VALUES (@IdPeminjaman, @IdPetugas, @TanggalKembali, @Keterlambatan, @Denda, @Catatan)
                """;
            await connection.ExecuteAsync(insertHeader, new
            {
                request.IdPeminjaman,
                IdPetugas = staffId,
                TanggalKembali = returnDate.ToDateTime(TimeOnly.MinValue),
                Keterlambatan = lateDays,
                Denda = lateFine,
                Catatan = AuthService.NormalizeOptional(request.Catatan)
            }, transaction);
            var returnId = checked((uint)await connection.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()", transaction: transaction));

            var conditionIds = await GetLookupIds(connection, transaction, "status_kondisi_kembali");
            var conditionsById = conditionIds.ToDictionary(value => value.Value, value => value.Key);
            var itemConditionIds = await GetLookupIds(connection, transaction, "status_barang");
            var availabilityIds = await GetLookupIds(connection, transaction, "status_ketersediaan");

            foreach (var unit in units)
            {
                if (!conditionsById.TryGetValue(unit.IdStatusKondisiKembali, out var condition))
                {
                    throw new AppException(400, $"idStatusKondisiKembali #{unit.IdStatusKondisiKembali} does not exist");
                }

                var conditionFine = condition switch
                {
                    "baik" => 0m,
                    "rusak_ringan" => denda.RusakRingan,
                    "rusak_berat" => denda.RusakBerat,
                    "hilang" => denda.Hilang,
                    _ => throw new AppException(400, $"Unsupported status_kondisi_kembali value '{condition}'")
                };
                ValidateFine(conditionFine, "dendaKondisi");

                var itemCondition = condition is "baik" or "rusak_ringan" ? condition : "rusak_berat";
                var availability = condition == "baik" ? "tersedia" : "tidak_tersedia";

                await connection.ExecuteAsync("""
                    INSERT INTO detail_pengembalian (
                        id_pengembalian,
                        id_unit_alat,
                        id_status_kondisi_kembali,
                        denda_kondisi,
                        catatan
                    )
                    VALUES (@IdPengembalian, @IdUnit, @IdKondisi, @DendaKondisi, @Catatan)
                    """, new
                {
                    IdPengembalian = returnId,
                    IdUnit = unit.IdUnitAlat,
                    IdKondisi = unit.IdStatusKondisiKembali,
                    DendaKondisi = conditionFine,
                    Catatan = AuthService.NormalizeOptional(unit.Catatan)
                }, transaction);
                await connection.ExecuteAsync("""
                    UPDATE unit_alat
                    SET id_status_barang = @IdStatusBarang,
                        id_status_ketersediaan = @IdStatusKetersediaan
                    WHERE id = @Id AND deleted_at IS NULL
                    """, new
                {
                    Id = unit.IdUnitAlat,
                    IdStatusBarang = RequireLookup(itemConditionIds, "status_barang", itemCondition),
                    IdStatusKetersediaan = RequireLookup(availabilityIds, "status_ketersediaan", availability)
                }, transaction);
            }

            var completedStatusId = await GetLoanStatusId(connection, transaction, "selesai");
            await connection.ExecuteAsync("""
                UPDATE peminjaman
                SET id_status_peminjaman = @StatusId
                WHERE id = @Id AND deleted_at IS NULL
                """, new { StatusId = completedStatusId, Id = request.IdPeminjaman }, transaction);
            await log.Write(connection, transaction, idUser, $"Memproses pengembalian peminjaman #{request.IdPeminjaman}");
            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static List<PengembalianUnitRequest> ValidateRequest(PengembalianRequest request)
    {
        if (request.IdPeminjaman == 0 || request.TanggalKembali is null)
        {
            throw new AppException(400, "idPeminjaman and tanggalKembali are required");
        }

        if (request.Units is not { Count: > 0 })
        {
            throw new AppException(400, "units must contain at least one unit");
        }

        if (request.Units.Any(unit => unit is null))
        {
            throw new AppException(400, "units must not contain null entries");
        }

        var units = request.Units.OfType<PengembalianUnitRequest>().ToList();

        if (units.Any(unit => unit.IdUnitAlat == 0 || unit.IdStatusKondisiKembali == 0))
        {
            throw new AppException(400, "Every unit requires idUnitAlat and idStatusKondisiKembali");
        }

        if (units.GroupBy(unit => unit.IdUnitAlat).Any(group => group.Count() > 1))
        {
            throw new AppException(400, "The same unit may only appear once in units");
        }

        return units;
    }

    private static void ValidateFine(decimal value, string field)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
        {
            throw new InvalidOperationException($"{field} must fit DECIMAL(15,2) UNSIGNED.");
        }
    }

    private static async Task<Dictionary<string, uint>> GetLookupIds(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string table)
    {
        var sql = table switch
        {
            "status_kondisi_kembali" => "SELECT id AS Id, nama AS Nama FROM status_kondisi_kembali",
            "status_barang" => "SELECT id AS Id, nama AS Nama FROM status_barang",
            "status_ketersediaan" => "SELECT id AS Id, nama AS Nama FROM status_ketersediaan",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        var rows = await connection.QueryAsync<LookupRow>(sql, transaction: transaction);
        return rows.ToDictionary(row => row.Nama, row => row.Id, StringComparer.Ordinal);
    }

    private static uint RequireLookup(Dictionary<string, uint> values, string table, string name)
    {
        return values.TryGetValue(name, out var id)
            ? id
            : throw new AppException(500, $"Seeded {table} value '{name}' is missing");
    }

    private static async Task<uint> GetLoanStatusId(
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

    private static async Task LoadDetails(MySqlConnection connection, List<PengembalianModel> returns)
    {
        if (returns.Count == 0)
        {
            return;
        }

        var details = await connection.QueryAsync<DetailPengembalianModel>("""
            SELECT dp.id AS Id,
                   dp.id_pengembalian AS IdPengembalian,
                   dp.id_unit_alat AS IdUnitAlat,
                   ua.kode_inventaris AS KodeInventaris,
                   a.nama AS NamaAlat,
                   dp.id_status_kondisi_kembali AS IdStatusKondisiKembali,
                   sk.nama AS StatusKondisiKembali,
                   dp.denda_kondisi AS DendaKondisi,
                   dp.catatan AS Catatan,
                   dpm.harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi
            FROM detail_pengembalian dp
            INNER JOIN unit_alat ua ON ua.id = dp.id_unit_alat
            INNER JOIN alat a ON a.id = ua.id_alat
            INNER JOIN status_kondisi_kembali sk ON sk.id = dp.id_status_kondisi_kembali
            INNER JOIN pengembalian pr ON pr.id = dp.id_pengembalian
            INNER JOIN detail_peminjaman dpm ON dpm.id_peminjaman = pr.id_peminjaman AND dpm.id_alat = ua.id_alat
            WHERE dp.id_pengembalian IN @Ids
            ORDER BY dp.id
            """, new { Ids = returns.Select(item => item.Id).ToArray() });
        var returnsById = returns.ToDictionary(item => item.Id);

        foreach (var detail in details)
        {
            if (returnsById.TryGetValue(detail.IdPengembalian, out var header))
            {
                header.Details.Add(detail);
            }
        }
    }

    private const string SelectSql = """
        SELECT pr.id AS Id,
               pr.id_peminjaman AS IdPeminjaman,
               p.id_peminjam AS IdPeminjam,
               u.username AS UsernamePeminjam,
               u.nama AS NamaPeminjam,
               pr.id_petugas AS IdPetugas,
               pt.nama AS NamaPetugas,
               sp.nama AS StatusPeminjaman,
               p.tanggal_peminjaman AS TanggalPeminjaman,
               p.tanggal_rencana_kembali AS TanggalRencanaKembali,
               pr.tanggal_kembali AS TanggalKembali,
               pr.keterlambatan_pengembalian AS KeterlambatanPengembalian,
               pr.denda AS Denda,
               pr.catatan AS Catatan,
               pr.deleted_at AS DeletedAt
        FROM pengembalian pr
        INNER JOIN peminjaman p ON p.id = pr.id_peminjaman
        INNER JOIN users u ON u.id = p.id_peminjam
        LEFT JOIN users pt ON pt.id = pr.id_petugas
        INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
        """;

    private sealed class LoanRow
    {
        public uint IdPeminjam { get; init; }
        public DateOnly TanggalPeminjaman { get; init; }
        public DateOnly TanggalRencanaKembali { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    private sealed class LookupRow
    {
        public uint Id { get; init; }
        public string Nama { get; init; } = string.Empty;
    }
}
