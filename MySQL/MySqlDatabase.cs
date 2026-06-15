#if MYSQL_MEL_LOGGING
using Microsoft.Extensions.Logging;
#endif

using System.Runtime.CompilerServices;
using MySqlConnector;

namespace MySQL;

public enum DbLogLevel { Debug, Info, Warn, Error }

public interface IDbLogger
{
    void Log(DbLogLevel level, string message, Exception? ex = null);
}

public static class DbLoggerExtensions
{
    public static void Debug(this IDbLogger l, string msg) => l.Log(DbLogLevel.Debug, msg);
    public static void Info(this IDbLogger l, string msg) => l.Log(DbLogLevel.Info, msg);
    public static void Warn(this IDbLogger l, string msg) => l.Log(DbLogLevel.Warn, msg);
    public static void Error(this IDbLogger l, string msg, Exception? ex = null) => l.Log(DbLogLevel.Error, msg, ex);
}

public sealed class NullDbLogger : IDbLogger
{
    public static readonly NullDbLogger Instance = new();
    private NullDbLogger() { }
    public void Log(DbLogLevel level, string message, Exception? ex = null) { }
}

public sealed class ConsoleDbLogger : IDbLogger
{
    public static readonly ConsoleDbLogger Instance = new();
    public DbLogLevel MinLevel { get; set; } = DbLogLevel.Info;
    private ConsoleDbLogger() { }

    public void Log(DbLogLevel level, string message, Exception? ex = null)
    {
        if (level < MinLevel) return;
        string tag = level switch
        {
            DbLogLevel.Debug => "DBG",
            DbLogLevel.Info => "INF",
            DbLogLevel.Warn => "WRN",
            DbLogLevel.Error => "ERR",
            _ => "???"
        };
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] MySQLLib: {message}");
        if (ex is not null) Console.WriteLine(ex);
    }
}

#if MYSQL_MEL_LOGGING
public sealed class MicrosoftLoggerAdapter : IDbLogger
{
    private readonly ILogger _inner;
    public MicrosoftLoggerAdapter(ILogger logger) => _inner = logger;
    public void Log(DbLogLevel level, string message, Exception? ex = null)
    {
        switch (level)
        {
            case DbLogLevel.Debug: _inner.LogDebug(message); break;
            case DbLogLevel.Info:  _inner.LogInformation(message); break;
            case DbLogLevel.Warn:  _inner.LogWarning(message); break;
            case DbLogLevel.Error: _inner.LogError(ex, message); break;
        }
    }
}

public sealed class MicrosoftLoggerAdapter<T> : IDbLogger
{
    private readonly ILogger<T> _inner;
    public MicrosoftLoggerAdapter(ILogger<T> logger) => _inner = logger;
    public void Log(DbLogLevel level, string message, Exception? ex = null)
    {
        switch (level)
        {
            case DbLogLevel.Debug: _inner.LogDebug(message); break;
            case DbLogLevel.Info:  _inner.LogInformation(message); break;
            case DbLogLevel.Warn:  _inner.LogWarning(message); break;
            case DbLogLevel.Error: _inner.LogError(ex, message); break;
        }
    }
}
#endif

public sealed class MySqlConnectionOptions
{
    public string Server { get; set; } = "localhost";
    public uint Port { get; set; } = 3306;
    public string Database { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Pooling { get; set; } = true;
    public uint MinPoolSize { get; set; } = 0;
    public uint MaxPoolSize { get; set; } = 100;
    public uint ConnectTimeout { get; set; } = 30;
    public bool AllowZeroDateTime { get; set; } = true;
    public bool ConvertZeroDateTime { get; set; } = true;
    public MySqlSslMode SslMode { get; set; } = MySqlSslMode.Preferred;

    public string? ToConnectionString()
    {
        MySqlConnectionStringBuilder b = new MySqlConnectionStringBuilder
        {
            Server = Server,
            Port = Port,
            Database = Database,
            UserID = UserId,
            Password = Password,
            Pooling = Pooling,
            MinimumPoolSize = MinPoolSize,
            MaximumPoolSize = MaxPoolSize,
            ConnectionTimeout = ConnectTimeout,
            AllowZeroDateTime = AllowZeroDateTime,
            ConvertZeroDateTime = ConvertZeroDateTime,
            SslMode = SslMode,
        };
        return b.ToString();
    }
}

public sealed record IndexDefinition(string Name, string[] Columns, bool Unique = false);

public sealed class TableSchema
{
    public string TableName { get; }
    public string? PrimaryKeyColumn { get; private set; }
    public Dictionary<string, string> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<IndexDefinition> Indexes { get; } = [];
    public TableSchema(string tableName) => TableName = tableName;

    public TableSchema PrimaryKey(string column, string definition)
    {
        PrimaryKeyColumn = column;
        Columns[column] = definition;
        return this;
    }

    public TableSchema Column(string name, string definition)
    {
        Columns[name] = definition;
        return this;
    }

    public TableSchema Index(string name, params string[] columns)
    {
        Indexes.Add(new IndexDefinition(name, columns, Unique: false));
        return this;
    }

    public TableSchema UniqueIndex(string name, params string[] columns)
    {
        Indexes.Add(new IndexDefinition(name, columns, Unique: true));
        return this;
    }
}

public sealed record ColumnInfo(string Name, string Type, bool Nullable, string Key, string? Default, string? Comment);

public sealed class DbRow
{
    private readonly Dictionary<string, object?> _data;
    private DbRow(Dictionary<string, object?> data) => _data = data;

    internal static DbRow FromReader(MySqlDataReader r)
    {
        Dictionary<string, object?> d = new Dictionary<string, object?>(r.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++)
            d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return new DbRow(d);
    }

    public IReadOnlyList<string> Columns => [.. _data.Keys];

    public object? this[string column] => _data.GetValueOrDefault(column);

    public object? this[int index] => _data.Values.ElementAtOrDefault(index);

    public T? Get<T>(string column)
    {
        if (!_data.TryGetValue(column, out object? v) || v is null) return default;
        if (v is T typed) return typed;
        return (T)Convert.ChangeType(v, typeof(T));
    }

    public T GetOrDefault<T>(string column, T fallback)
    {
        T? r = Get<T>(column);
        return r is null ? fallback : r;
    }

    public bool TryGet<T>(string column, out T? value)
    {
        try { value = Get<T>(column); return value is not null; }
        catch { value = default; return false; }
    }

    public string GetString(string col) => Get<string>(col) ?? string.Empty;
    public int GetInt(string col) => Get<int>(col);
    public long GetLong(string col) => Get<long>(col);
    public float GetFloat(string col) => Get<float>(col);
    public double GetDouble(string col) => Get<double>(col);
    public decimal GetDecimal(string col) => Get<decimal>(col);
    public bool GetBool(string col) => Get<bool>(col);
    public DateTime GetDateTime(string col) => Get<DateTime>(col);
    public Guid GetGuid(string col) => Get<Guid>(col);
    public byte[] GetBytes(string col) => Get<byte[]>(col) ?? [];

    public bool IsNull(string col) => !_data.TryGetValue(col, out object? v) || v is null;

    public Dictionary<string, object?> ToDictionary() => new(_data, StringComparer.OrdinalIgnoreCase);
}

public static class Params
{
    public static Dictionary<string, object?> Of(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
    public static Dictionary<string, object?> Of(string key, object? value) => new() { [key] = value };
    public static Dictionary<string, object?> Empty() => [];
}

public sealed class SelectBuilder
{
    private string _table = string.Empty;
    private List<string> _cols = ["*"];
    private readonly List<string> _wheres = [];
    private readonly List<string> _joins = [];
    private readonly List<string> _groupBy = [];
    private readonly List<string> _having = [];
    private readonly Dictionary<string, object?> _params = [];
    private string? _orderBy;
    private bool _orderDesc;
    private int? _limit;
    private int? _offset;
    public SelectBuilder From(string table) { _table = table; return this; }
    public SelectBuilder Select(params string[] columns) { _cols = [.. columns]; return this; }
    public SelectBuilder Limit(int limit) { _limit = limit; return this; }
    public SelectBuilder Offset(int offset) { _offset = offset; return this; }

    public SelectBuilder OrderBy(string column, bool descending = false)
    {
        _orderBy = column;
        _orderDesc = descending;
        return this;
    }

    public SelectBuilder Where(string condition, Dictionary<string, object?>? parameters = null)
    {
        _wheres.Add(condition);
        if (parameters is not null)
            foreach (KeyValuePair<string, object?> kv in parameters) _params[kv.Key] = kv.Value;
        return this;
    }

    public SelectBuilder GroupBy(params string[] columns)
    {
        _groupBy.AddRange(columns);
        return this;
    }

    public SelectBuilder Having(string condition, Dictionary<string, object?>? parameters = null)
    {
        _having.Add(condition);
        if (parameters is not null)
            foreach (KeyValuePair<string, object?> kv in parameters) _params[kv.Key] = kv.Value;
        return this;
    }

    public SelectBuilder Join(string table, string onCondition, string type = "INNER")
    {
        _joins.Add($"{type} JOIN `{table}` ON {onCondition}");
        return this;
    }

    public (string Sql, Dictionary<string, object?> Parameters) Build()
    {
        if (string.IsNullOrWhiteSpace(_table))
            throw new InvalidOperationException("Call From(tableName) before building.");
        string sql = $"SELECT {string.Join(", ", _cols)} FROM `{_table}`";
        if (_joins.Count > 0) sql += " " + string.Join(" ", _joins);
        if (_wheres.Count > 0) sql += " WHERE " + string.Join(" AND ", _wheres);
        if (_groupBy.Count > 0) sql += " GROUP BY " + string.Join(", ", _groupBy.Select(c => $"`{c}`"));
        if (_having.Count > 0) sql += " HAVING " + string.Join(" AND ", _having);
        if (_orderBy is not null) sql += $" ORDER BY `{_orderBy}`" + (_orderDesc ? " DESC" : " ASC");
        if (_limit.HasValue) sql += " LIMIT " + _limit;
        if (_offset.HasValue) sql += " OFFSET " + _offset;
        return (sql, new Dictionary<string, object?>(_params));
    }

    public async Task<List<DbRow>> RunAsync(MySqlDatabase db, CancellationToken ct = default)
    {
        (string? sql, Dictionary<string, object?>? parameters) = Build();
        return await db.QueryAsync(sql, parameters, ct);
    }

    public async Task<List<DbRow>> RunAsync(MySqlDatabaseTransaction transaction, CancellationToken ct = default)
    {
        (string? sql, Dictionary<string, object?>? parameters) = Build();
        return await transaction.QueryAsync(sql, parameters, ct);
    }
}

public sealed class MySqlDatabaseTransaction : IAsyncDisposable
{
    private readonly MySqlConnection _conn;
    private readonly MySqlTransaction _tx;
    private readonly IDbLogger _logger;
    private bool _done;

    internal MySqlDatabaseTransaction(MySqlConnection conn, MySqlTransaction tx, IDbLogger logger)
    {
        _conn = conn;
        _tx = tx;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string sql, Dictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlCommand cmd = BuildCmd(sql, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> ScalarAsync<T>(string sql, Dictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlCommand cmd = BuildCmd(sql, parameters);
        object? v = await cmd.ExecuteScalarAsync(ct);
        if (v is null or DBNull) return default;
        return (T)Convert.ChangeType(v, typeof(T));
    }

    public async Task<List<DbRow>> QueryAsync(string sql, Dictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlCommand cmd = BuildCmd(sql, parameters);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        List<DbRow> rows = new List<DbRow>();
        while (await reader.ReadAsync(ct))
            rows.Add(DbRow.FromReader(reader));
        return rows;
    }

    public Task SavepointAsync(string name, CancellationToken ct = default) => _tx.SaveAsync(name, ct);

    public Task RollbackToAsync(string name, CancellationToken ct = default) => _tx.RollbackAsync(name, ct);

    public Task ReleaseSavepointAsync(string name, CancellationToken ct = default) => _tx.ReleaseAsync(name, ct);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _tx.CommitAsync(ct);
        _done = true;
        _logger.Debug("Transaction committed.");
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _tx.RollbackAsync(ct);
        _done = true;
        _logger.Debug("Transaction rolled back.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_done)
        {
            _logger.Warn("Transaction disposed without commit — rolling back.");
            try { await _tx.RollbackAsync(); } catch { /* connection may already be gone */ }
        }
        await _tx.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private MySqlCommand BuildCmd(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters)
    {
        MySqlCommand cmd = new MySqlCommand(sql, _conn, _tx);
        CommandHelper.ApplyParameters(cmd, parameters);
        return cmd;
    }
}

public sealed class MySqlDatabase
{
    private readonly string _cs;
    private readonly IDbLogger _logger;

    public MySqlDatabase(string connectionString, IDbLogger? logger = null)
    {
        _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? NullDbLogger.Instance;
    }

    public MySqlDatabase(MySqlConnectionOptions options, IDbLogger? logger = null) : this(options?.ToConnectionString() ?? throw new ArgumentNullException(nameof(options)), logger) { }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        MySqlConnection conn = new MySqlConnection(_cs);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<int> ExecuteAsync(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlConnection conn = await OpenAsync(ct);
        await using MySqlCommand cmd = new MySqlCommand(sql, conn);
        CommandHelper.ApplyParameters(cmd, parameters);
        try
        {
            int n = await cmd.ExecuteNonQueryAsync(ct);
            _logger.Debug($"Execute ({n} rows): {sql}");
            return n;
        }
        catch (Exception ex)
        {
            _logger.Error($"Execute failed: {sql}", ex);
            throw;
        }
    }

    public async Task<T?> ScalarAsync<T>(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlConnection conn = await OpenAsync(ct);
        await using MySqlCommand cmd = new MySqlCommand(sql, conn);
        CommandHelper.ApplyParameters(cmd, parameters);
        try
        {
            object? v = await cmd.ExecuteScalarAsync(ct);
            if (v is null or DBNull) return default;
            return (T)Convert.ChangeType(v, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.Error($"Scalar failed: {sql}", ex);
            throw;
        }
    }

    public async Task<List<DbRow>> QueryAsync(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters = null, CancellationToken ct = default)
    {
        await using MySqlConnection conn = await OpenAsync(ct);
        await using MySqlCommand cmd = new MySqlCommand(sql, conn);
        CommandHelper.ApplyParameters(cmd, parameters);
        List<DbRow> rows = new List<DbRow>();
        try
        {
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(DbRow.FromReader(reader));
            _logger.Debug($"Query returned {rows.Count} rows: {sql}");
            return rows;
        }
        catch (Exception ex)
        {
            _logger.Error($"Query failed: {sql}", ex);
            throw;
        }
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<DbRow, T> map, IEnumerable<KeyValuePair<string, object?>>? parameters = null, CancellationToken ct = default)
    {
        List<DbRow> rows = await QueryAsync(sql, parameters, ct);
        return rows.Select(map).ToList();
    }

    public async Task<DbRow?> QuerySingleAsync(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters = null, CancellationToken ct = default)
    {
        List<DbRow> rows = await QueryAsync(sql, parameters, ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async IAsyncEnumerable<DbRow> QueryStreamAsync(string sql, IEnumerable<KeyValuePair<string, object?>>? parameters = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using MySqlConnection conn = await OpenAsync(ct);
        await using MySqlCommand cmd = new MySqlCommand(sql, conn);
        CommandHelper.ApplyParameters(cmd, parameters);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return DbRow.FromReader(reader);
    }

    public Task<int> InsertAsync(string table, Dictionary<string, object?> values, CancellationToken ct = default)
    {
        if (values.Count == 0) throw new ArgumentException("No values provided.", nameof(values));
        string cols = string.Join(", ", values.Keys.Select(k => $"`{k}`"));
        string pars = string.Join(", ", values.Keys.Select(k => $"@{k}"));
        return ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES ({pars})", values, ct);
    }

    public async Task<long> InsertGetIdAsync(string table, Dictionary<string, object?> values, CancellationToken ct = default)
    {
        if (values.Count == 0) throw new ArgumentException("No values provided.", nameof(values));
        string cols = string.Join(", ", values.Keys.Select(k => $"`{k}`"));
        string pars = string.Join(", ", values.Keys.Select(k => $"@{k}"));
        await using MySqlConnection conn = await OpenAsync(ct);
        await using MySqlCommand cmd = new MySqlCommand($"INSERT INTO `{table}` ({cols}) VALUES ({pars})", conn);
        CommandHelper.ApplyParameters(cmd, values);
        await cmd.ExecuteNonQueryAsync(ct);
        return cmd.LastInsertedId;
    }

    public Task<int> UpsertAsync(string table, Dictionary<string, object?> values, CancellationToken ct = default)
    {
        if (values.Count == 0) throw new ArgumentException("No values provided.", nameof(values));
        string cols = string.Join(", ", values.Keys.Select(k => $"`{k}`"));
        string pars = string.Join(", ", values.Keys.Select(k => $"@{k}"));
        string update = string.Join(", ", values.Keys.Select(k => $"`{k}` = VALUES(`{k}`)"));
        return ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES ({pars}) ON DUPLICATE KEY UPDATE {update}", values, ct);
    }

    public Task<int> UpdateAsync(string table, Dictionary<string, object?> setValues, string where, Dictionary<string, object?>? whereParams = null, CancellationToken ct = default)
    {
        if (setValues.Count == 0)
            throw new ArgumentException("No SET values provided.", nameof(setValues));
        if (string.IsNullOrWhiteSpace(where))
            throw new ArgumentException("WHERE clause is required.", nameof(where));
        string setClause = string.Join(", ", setValues.Keys.Select(k => $"`{k}` = @s_{k}"));
        Dictionary<string, object?> allParams = setValues.ToDictionary(kv => "s_" + kv.Key, kv => kv.Value);
        if (whereParams is not null)
            foreach (KeyValuePair<string, object?> kv in whereParams) allParams[kv.Key] = kv.Value;
        return ExecuteAsync($"UPDATE `{table}` SET {setClause} WHERE {where}", allParams, ct);
    }

    public Task<int> DeleteAsync(string table, string where, Dictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(where))
            throw new ArgumentException(
                "WHERE clause is required. To intentionally delete all rows, use TRUNCATE TABLE or pass \"1=1\".", nameof(where));
        return ExecuteAsync($"DELETE FROM `{table}` WHERE {where}", parameters, ct);
    }

    public async Task<bool> ExistsAsync(string table,string where,Dictionary<string, object?>? parameters = null,CancellationToken ct = default) => await ScalarAsync<long>($"SELECT COUNT(1) FROM `{table}` WHERE {where}", parameters, ct) > 0;

    public Task<int> BulkInsertAsync(string table, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return Task.FromResult(0);
        List<string> keys = rows[0].Keys.ToList();
        string cols = string.Join(", ", keys.Select(k => $"`{k}`"));
        List<string> groups = new List<string>(rows.Count);
        Dictionary<string, object?> allPars = new Dictionary<string, object?>(rows.Count * keys.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            groups.Add("(" + string.Join(", ", keys.Select(k => $"@r{i}_{k}")) + ")");
            foreach (string k in keys)
                allPars[$"r{i}_{k}"] = rows[i].GetValueOrDefault(k);
        }
        _logger.Debug($"BulkInsert {rows.Count} rows into `{table}`");
        return ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES {string.Join(", ", groups)}", allPars, ct);
    }

    public async Task<int> BulkInsertChunkedAsync(string table, IReadOnlyList<Dictionary<string, object?>> rows, int chunkSize = 500, CancellationToken ct = default)
    {
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (rows.Count == 0) return 0;
        int total = 0;
        int chunks = (int)Math.Ceiling(rows.Count / (double)chunkSize);
        _logger.Info($"BulkInsertChunked: {rows.Count} rows into `{table}` in {chunks} chunk(s) of {chunkSize}");
        for (int i = 0; i < rows.Count; i += chunkSize)
        {
            List<Dictionary<string, object?>> chunk = rows.Skip(i).Take(chunkSize).ToList();
            total += await BulkInsertAsync(table, chunk, ct);
        }
        return total;
    }

    public async Task<int> BulkInsertTransactedAsync(string table, IReadOnlyList<Dictionary<string, object?>> rows, int chunkSize = 500, CancellationToken ct = default)
    {
        if (rows.Count == 0) return 0;
        int total = 0;
        await using MySqlDatabaseTransaction tx = await BeginTransactionAsync(ct);
        try
        {
            for (int i = 0; i < rows.Count; i += chunkSize)
            {
                List<Dictionary<string, object?>> chunk = rows.Skip(i).Take(chunkSize).ToList();
                List<string> keys = chunk[0].Keys.ToList();
                string cols = string.Join(", ", keys.Select(k => $"`{k}`"));
                List<string> groups = new List<string>(chunk.Count);
                Dictionary<string, object?> allPars = new Dictionary<string, object?>(chunk.Count * keys.Count);
                for (int j = 0; j < chunk.Count; j++)
                {
                    groups.Add("(" + string.Join(", ", keys.Select(k => $"@r{j}_{k}")) + ")");
                    foreach (string k in keys)
                        allPars[$"r{j}_{k}"] = chunk[j].GetValueOrDefault(k);
                }
                total += await tx.ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES {string.Join(", ", groups)}", allPars, ct);
            }
            await tx.CommitAsync(ct);
            _logger.Info($"BulkInsertTransacted: committed {total} rows into `{table}`");
            return total;
        }
        catch (Exception ex)
        {
            _logger.Error($"BulkInsertTransacted: rolling back {table}", ex);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<MySqlDatabaseTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        MySqlConnection conn = await OpenAsync(ct);
        MySqlTransaction tx = await conn.BeginTransactionAsync(ct);
        return new MySqlDatabaseTransaction(conn, tx, _logger);
    }

    public async Task TransactAsync(Func<MySqlDatabaseTransaction, Task> work, CancellationToken ct = default)
    {
        await using MySqlDatabaseTransaction tx = await BeginTransactionAsync(ct);
        try
        {
            await work(tx);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.Error("Transaction failed — rolling back.", ex);
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<string>> GetTablesAsync(CancellationToken ct = default)
    {
        List<DbRow> rows = await QueryAsync("SHOW TABLES", null, ct);
        return rows.Select(r => r.GetString(r.Columns[0])).ToList();
    }

    public async Task<bool> TableExistsAsync(string table, CancellationToken ct = default) => (await GetTablesAsync(ct)).Contains(table, StringComparer.OrdinalIgnoreCase);

    public async Task<List<ColumnInfo>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        List<DbRow> rows = await QueryAsync($"SHOW FULL COLUMNS FROM `{table}`", null, ct);
        return rows.Select(r => new ColumnInfo(r.GetString("Field"), r.GetString("Type"), r.GetString("Null") == "YES", r.GetString("Key"), r.IsNull("Default") ? null : r.GetString("Default"), r.IsNull("Comment") ? null : r.GetString("Comment"))).ToList();
    }

    public async Task<HashSet<string>> GetIndexNamesAsync(string table, CancellationToken ct = default)
    {
        List<DbRow> rows = await QueryAsync($"SHOW INDEX FROM `{table}`", null, ct);
        return rows.Select(r => r.GetString("Key_name")).Where(n => !string.Equals(n, "PRIMARY", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task EnsureIndexAsync(string table, string indexName, string[] columns, bool unique = false, CancellationToken ct = default)
    {
        HashSet<string> existing = await GetIndexNamesAsync(table, ct);
        if (existing.Contains(indexName)) return;
        string colList = string.Join(", ", columns.Select(c => $"`{c}`"));
        string modifier = unique ? "UNIQUE " : "";
        await ExecuteAsync($"CREATE {modifier}INDEX `{indexName}` ON `{table}` ({colList})", null, ct);
        _logger.Info($"Created {(unique ? "unique " : "")}index '{indexName}' on '{table}' ({colList})");
    }

    public async Task EnsureTableAsync(TableSchema schema, CancellationToken ct = default)
    {
        if (await TableExistsAsync(schema.TableName, ct))
        {
            HashSet<string> existing = (await GetColumnsAsync(schema.TableName, ct)).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach ((string? col, string? def) in schema.Columns)
                if (!existing.Contains(col))
                {
                    await ExecuteAsync($"ALTER TABLE `{schema.TableName}` ADD COLUMN `{col}` {def}", null, ct);
                    _logger.Info($"Added column '{col}' to '{schema.TableName}'");
                }
        }
        else
        {
            List<string> colDefs = schema.Columns.Select(kv => $"`{kv.Key}` {kv.Value}").ToList();
            if (schema.PrimaryKeyColumn is not null)
                colDefs.Add($"PRIMARY KEY (`{schema.PrimaryKeyColumn}`)");
            await ExecuteAsync($"CREATE TABLE IF NOT EXISTS `{schema.TableName}` ({string.Join(", ", colDefs)})", null, ct);
            _logger.Info($"Created table '{schema.TableName}'");
        }
        foreach (IndexDefinition idx in schema.Indexes)
            await EnsureIndexAsync(schema.TableName, idx.Name, idx.Columns, idx.Unique, ct);
    }

    public async Task EnsureTableAsync(string table, Dictionary<string, string> columnDefs, string? primaryKey = null, CancellationToken ct = default)
    {
        TableSchema s = new TableSchema(table);
        foreach ((string? col, string? def) in columnDefs)
            if (primaryKey is not null && col == primaryKey) s.PrimaryKey(col, def);
            else s.Column(col, def);
        await EnsureTableAsync(s, ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await using MySqlConnection conn = await OpenAsync(ct);
            return conn.State == System.Data.ConnectionState.Open;
        }
        catch { return false; }
    }

    public async Task<string> GetServerVersionAsync(CancellationToken ct = default)
    {
        await using MySqlConnection conn = await OpenAsync(ct);
        return conn.ServerVersion;
    }
}

file static class CommandHelper
{
    internal static void ApplyParameters(MySqlCommand cmd, IEnumerable<KeyValuePair<string, object?>>? parameters)
    {
        if (parameters is null) return;
        foreach ((string? key, object? value) in parameters)
            cmd.Parameters.AddWithValue("@" + key.TrimStart('@'), value ?? DBNull.Value);
    }
}