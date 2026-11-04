using Dapper;
using MySqlConnector;
using UKK.Models;

namespace UKK.Services;

public sealed class AuthService
{
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(
        Guid.NewGuid().ToString("N"),
        12);

    private readonly Database db;
    private readonly LogAktivitasService log;
    private readonly JwtService jwt;
    private readonly RecaptchaService recaptcha;

    public AuthService(
        Database database,
        LogAktivitasService activityLog,
        JwtService jwtService,
        RecaptchaService recaptchaService)
    {
        db = database;
        log = activityLog;
        jwt = jwtService;
        recaptcha = recaptchaService;
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        var username = NormalizeRequired(request.Username, "username", 50);
        ValidatePassword(request.Password, required: true);

        if (!await recaptcha.VerifyAsync(request.CaptchaToken, remoteIp, cancellationToken))
        {
            throw new AppException(400, "Captcha verification failed");
        }

        const string sql = """
            SELECT u.id AS Id,
                   u.id_role AS IdRole,
                   u.username AS Username,
                   u.nama AS Nama,
                   u.password AS PasswordHash,
                   u.no_telepon AS NoTelepon,
                   u.alamat AS Alamat,
                   r.nama AS Role,
                   u.created_at AS CreatedAt,
                   u.deleted_at AS DeletedAt
            FROM users u
            INNER JOIN roles r ON r.id = u.id_role
            WHERE u.username = @Username
              AND u.deleted_at IS NULL
            LIMIT 1
            """;

        await using var connection = db.Connect();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Username = username }, cancellationToken: cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserModel>(command);

        var passwordValid = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user?.PasswordHash ?? DummyPasswordHash);

        if (user is null || !passwordValid)
        {
            await log.Write(null, $"Login gagal untuk username '{username}'.");
            throw new AppException(401, "Username atau password salah");
        }

        await log.Write(user.Id, $"Login berhasil untuk username '{user.Username}'.");
        return jwt.CreateToken(ToResponse(user));
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var username = NormalizeRequired(request.Username, "username", 50);
        var name = NormalizeRequired(request.Nama, "nama", 100);
        var phone = NormalizePhone(request.NoTelepon);
        ValidatePassword(request.Password, required: true);

        await using var connection = db.Connect();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureUniqueAsync(connection, transaction, username, phone, null);

            const string roleSql = "SELECT id FROM roles WHERE nama = 'Peminjam' LIMIT 1";
            var roleId = await connection.QuerySingleOrDefaultAsync<uint?>(roleSql, transaction: transaction);

            if (roleId is null)
            {
                throw new AppException(500, "Seeded role 'Peminjam' is missing");
            }

            const string insertSql = """
                INSERT INTO users (id_role, username, nama, password, no_telepon, alamat)
                VALUES (@IdRole, @Username, @Nama, @PasswordHash, @NoTelepon, @Alamat)
                """;

            await connection.ExecuteAsync(insertSql, new
            {
                IdRole = roleId.Value,
                Username = username,
                Nama = name,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                NoTelepon = phone,
                Alamat = NormalizeOptional(request.Alamat)
            }, transaction);

            var id = checked((uint)await connection.ExecuteScalarAsync<ulong>("SELECT LAST_INSERT_ID()", transaction: transaction));
            var user = await GetByIdAsync(connection, transaction, id);
            await log.Write(connection, transaction, id, $"Registrasi akun Peminjam '{username}'.");
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(user);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task LogoutAsync(uint userId)
    {
        return log.Write(userId, "Logout berhasil.");
    }

    internal static UserResponse ToResponse(UserModel user)
    {
        return new UserResponse
        {
            Id = user.Id,
            IdRole = user.IdRole,
            Username = user.Username,
            Nama = user.Nama,
            NoTelepon = user.NoTelepon,
            Alamat = user.Alamat,
            Role = user.Role,
            CreatedAt = user.CreatedAt
        };
    }

    internal static async Task EnsureUniqueAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string username,
        string phone,
        uint? excludedId)
    {
        const string sql = """
            SELECT EXISTS(
                       SELECT 1 FROM users
                       WHERE username = @Username
                         AND (@ExcludedId IS NULL OR id <> @ExcludedId)
                   ) AS UsernameExists,
                   EXISTS(
                       SELECT 1 FROM users
                       WHERE no_telepon = @NoTelepon
                         AND (@ExcludedId IS NULL OR id <> @ExcludedId)
                   ) AS PhoneExists
            """;

        var duplicate = await connection.QuerySingleAsync<DuplicateUserCheck>(
            sql,
            new { Username = username, NoTelepon = phone, ExcludedId = excludedId },
            transaction);

        if (duplicate.UsernameExists)
        {
            throw new AppException(409, "Username sudah digunakan.");
        }

        if (duplicate.PhoneExists)
        {
            throw new AppException(409, "Nomor telepon sudah digunakan.");
        }
    }

    internal static string NormalizeRequired(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;

        if (normalized.Length == 0)
        {
            throw new AppException(400, $"{field} is required");
        }

        if (normalized.Length > maxLength)
        {
            throw new AppException(400, $"{field} must not exceed {maxLength} characters");
        }

        return normalized;
    }

    internal static string NormalizePhone(string? value)
    {
        return NormalizeRequired(value, "noTelepon", 20);
    }

    internal static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static void ValidatePassword(string? password, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(password))
        {
            throw new AppException(400, "password is required");
        }

        if (!string.IsNullOrEmpty(password) && System.Text.Encoding.UTF8.GetByteCount(password) > 72)
        {
            throw new AppException(400, "password must not exceed 72 UTF-8 bytes");
        }
    }

    private static async Task<UserModel> GetByIdAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        uint id)
    {
        const string sql = """
            SELECT u.id AS Id,
                   u.id_role AS IdRole,
                   u.username AS Username,
                   u.nama AS Nama,
                   u.no_telepon AS NoTelepon,
                   u.alamat AS Alamat,
                   r.nama AS Role,
                   u.created_at AS CreatedAt,
                   u.deleted_at AS DeletedAt
            FROM users u
            INNER JOIN roles r ON r.id = u.id_role
            WHERE u.id = @Id
              AND u.deleted_at IS NULL
            LIMIT 1
            """;

        return await connection.QuerySingleAsync<UserModel>(sql, new { Id = id }, transaction);
    }

    private sealed class DuplicateUserCheck
    {
        public bool UsernameExists { get; init; }
        public bool PhoneExists { get; init; }
    }
}
