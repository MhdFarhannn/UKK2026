using MySqlConnector;

namespace UKK.Services;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
        }
    }

    public MySqlConnection Connect()
    {
        return new MySqlConnection(_connectionString);
    }
}
