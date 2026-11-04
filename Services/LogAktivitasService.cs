using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services
{
    public class LogAktivitasService
    {
        private readonly Database db;

        public LogAktivitasService(Database _db)
        {
            db = _db;
        }

        public async Task Write(uint? idUser, string aktivitas)
        {
            await using var conn = db.Connect();

            await conn.ExecuteAsync(InsertSql, new { IdUser = idUser, Aktivitas = aktivitas });
        }

        public Task Write(
            MySqlConnection connection,
            MySqlTransaction transaction,
            uint? idUser,
            string aktivitas)
        {
            return connection.ExecuteAsync(InsertSql, new { IdUser = idUser, Aktivitas = aktivitas }, transaction);
        }

        public async Task<PagedResult<LogAktivitasModel>> GetAllLogAktivitas(
            int page,
            int pageSize,
            uint? idUser,
            DateOnly? from,
            DateOnly? to)
        {
            (page, pageSize) = Pagination.Normalize(page, pageSize);
            await using var conn = db.Connect();

            const string countQuery = @"
                SELECT COUNT(*)
                FROM log_aktivitas l
                WHERE (@IdUser IS NULL OR l.id_user = @IdUser)
                  AND (@From IS NULL OR l.waktu >= @From)
                  AND (@To IS NULL OR l.waktu < DATE_ADD(@To, INTERVAL 1 DAY))";

            const string query = @"
                SELECT l.id AS Id,
                       l.id_user AS IdUser,
                       u.username AS Username,
                       u.nama AS NamaUser,
                       l.aktivitas AS Aktivitas,
                       l.waktu AS Waktu
                FROM log_aktivitas l
                LEFT JOIN users u ON u.id = l.id_user
                WHERE (@IdUser IS NULL OR l.id_user = @IdUser)
                  AND (@From IS NULL OR l.waktu >= @From)
                  AND (@To IS NULL OR l.waktu < DATE_ADD(@To, INTERVAL 1 DAY))
                ORDER BY l.id DESC
                LIMIT @Limit OFFSET @Offset";

            var param = new
            {
                IdUser = idUser,
                From = from?.ToDateTime(TimeOnly.MinValue),
                To = to?.ToDateTime(TimeOnly.MinValue),
                Limit = pageSize,
                Offset = Pagination.Offset(page, pageSize)
            };

            var total = await conn.ExecuteScalarAsync<long>(countQuery, param);
            var result = await conn.QueryAsync<LogAktivitasModel>(query, param);

            return new PagedResult<LogAktivitasModel> { Items = result.ToList(), Page = page, PageSize = pageSize, Total = total };
        }

        private const string InsertSql = """
            INSERT INTO log_aktivitas (id_user, aktivitas)
            VALUES (@IdUser, @Aktivitas)
            """;
    }
}
