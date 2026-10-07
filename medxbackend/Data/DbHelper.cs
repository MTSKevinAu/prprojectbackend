using Npgsql;
using System.Data;

namespace medxbackend.Data;

public sealed class DbHelper
{
    private readonly string _connectionString;

    public DbHelper(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PostgreSql is not configured.");
    }

    public async Task<int> ExecuteNonQueryAsync(
        string sql,
        IEnumerable<NpgsqlParameter>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        IEnumerable<NpgsqlParameter>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellationToken);

        if (value is null or DBNull)
        {
            return default;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql,
        Func<NpgsqlDataReader, T> map,
        IEnumerable<NpgsqlParameter>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        IEnumerable<NpgsqlParameter>? parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        if (parameters is not null)
        {
            command.Parameters.AddRange(parameters.ToArray());
        }

        return command;
    }
}
