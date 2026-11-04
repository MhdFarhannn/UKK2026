using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class UsersService
{
    private readonly Database db;
    private readonly LogAktivitasService log;

    public UsersService(Database database, LogAktivitasService activityLog)
    {
        db = database;
        log = activityLog;
    }

    public async Task<PagedResult<UserResponse>> GetAllUsers(
        int page,
        int pageSize,
        string? search,
        uint? idRole)
    {
        (page, pageSize) = Pagination.Normalize(page, pageSize);
        await using var connection = db.Connect();

        const string filter = """
            FROM users u
            INNER JOIN roles r ON r.id = u.id_role
            WHERE u.deleted_at IS NULL
              AND (@Search IS NULL
                   OR u.username LIKE CONCAT('%', @Search, '%')
                   OR u.nama LIKE CONCAT('%', @Search, '%')
                   OR u.no_telepon LIKE CONCAT('%', @Search, '%'))
              AND (@IdRole IS NULL OR u.id_role = @IdRole)
            """;

        const string query = """
            SELECT u.id AS Id,
                   u.id_role AS IdRole,
                   u.username AS Username,
                   u.nama AS Nama,
                   u.no_telepon AS NoTelepon,
                   u.alamat AS Alamat,
                   r.nama AS Role,
                   u.created_at AS CreatedAt
            """ + " " + filter + " " + """
            ORDER BY u.id
            LIMIT @Limit OFFSET @Offset
            """;

        var parameters = new
        {
            Search = Pagination.Search(search),
            IdRole = idRole,
            Limit = pageSize,
            Offset = Pagination.Offset(page, pageSize)
        };

        var total = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) " + filter, parameters);
        var items = (await connection.QueryAsync<UserResponse>(query, parameters)).ToList();

        return new PagedResult<UserResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total
        };
    }

    public async Task<UserResponse> GetUserById(uint id)
    {
        await using var connection = db.Connect();
        var result = await FindUserAsync(connection, null, id, false);
        return result ?? throw new AppException(404, "User not found");
    }

    public async Task<bool> CreateUser(UserRequest request, uint idAdmin)
    {
        var username = AuthService.NormalizeRequired(request.Username, "username", 50);
        var name = AuthService.NormalizeRequired(request.Nama, "nama", 100);
        var phone = AuthService.NormalizePhone(request.NoTelepon);
        AuthService.ValidatePassword(request.Password, required: true);

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await EnsureRoleExists(connection, transaction, request.IdRole);
            await AuthService.EnsureUniqueAsync(connection, transaction, username, phone, null);

            const string sql = """
                INSERT INTO users (id_role, username, nama, password, no_telepon, alamat)
                VALUES (@IdRole, @Username, @Nama, @Password, @NoTelepon, @Alamat)
                """;

            var affected = await connection.ExecuteAsync(sql, new
            {
                request.IdRole,
                Username = username,
                Nama = name,
                Password = BCrypt.Net.BCrypt.HashPassword(request.Password!, 12),
                NoTelepon = phone,
                Alamat = AuthService.NormalizeOptional(request.Alamat)
            }, transaction);

            await log.Write(connection, transaction, idAdmin, $"Menambah user {username}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> UpdateUser(uint id, UserRequest request, uint idAdmin)
    {
        var username = AuthService.NormalizeRequired(request.Username, "username", 50);
        var name = AuthService.NormalizeRequired(request.Nama, "nama", 100);
        var phone = AuthService.NormalizePhone(request.NoTelepon);
        AuthService.ValidatePassword(request.Password, required: false);

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindUserAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "User not found");

            await EnsureRoleExists(connection, transaction, request.IdRole);
            await AuthService.EnsureUniqueAsync(connection, transaction, username, phone, id);

            const string sql = """
                UPDATE users
                SET id_role = @IdRole,
                    username = @Username,
                    nama = @Nama,
                    no_telepon = @NoTelepon,
                    alamat = @Alamat,
                    password = COALESCE(@Password, password)
                WHERE id = @Id
                  AND deleted_at IS NULL
                """;

            var affected = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                request.IdRole,
                Username = username,
                Nama = name,
                NoTelepon = phone,
                Alamat = AuthService.NormalizeOptional(request.Alamat),
                Password = string.IsNullOrWhiteSpace(request.Password)
                    ? null
                    : BCrypt.Net.BCrypt.HashPassword(request.Password, 12)
            }, transaction);

            await log.Write(connection, transaction, idAdmin, $"Mengubah user {current.Username}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> DeleteUser(uint id, uint idAdmin)
    {
        if (id == idAdmin)
        {
            throw new AppException(409, "You cannot delete your own account");
        }

        await using var connection = db.Connect();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var current = await FindUserAsync(connection, transaction, id, true)
                ?? throw new AppException(404, "User not found");

            const string sql = """
                UPDATE users
                SET deleted_at = CURRENT_TIMESTAMP
                WHERE id = @Id
                  AND deleted_at IS NULL
                """;

            var affected = await connection.ExecuteAsync(sql, new { Id = id }, transaction);
            await log.Write(connection, transaction, idAdmin, $"Menghapus user {current.Username}");
            await transaction.CommitAsync();
            return affected == 1;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<UserResponse?> FindUserAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        uint id,
        bool forUpdate)
    {
        var sql = """
            SELECT u.id AS Id,
                   u.id_role AS IdRole,
                   u.username AS Username,
                   u.nama AS Nama,
                   u.no_telepon AS NoTelepon,
                   u.alamat AS Alamat,
                   r.nama AS Role,
                   u.created_at AS CreatedAt
            FROM users u
            INNER JOIN roles r ON r.id = u.id_role
            WHERE u.id = @Id
              AND u.deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE" : string.Empty);

        return await connection.QuerySingleOrDefaultAsync<UserResponse>(sql, new { Id = id }, transaction);
    }

    private static async Task EnsureRoleExists(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint idRole)
    {
        if (idRole == 0 || !await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM roles WHERE id = @Id)",
                new { Id = idRole },
                transaction))
        {
            throw new AppException(400, "idRole does not reference a role");
        }
    }
}
