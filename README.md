# MySqlDatabase

A clean, async-first MySQL library for .NET built on top of [MySqlConnector](https://mysqlconnector.net/). Wraps connection management, parameterized queries, transactions, schema introspection, and bulk operations behind a straightforward API with zero boilerplate.

---

## Features

- **Async everywhere** — every method is fully `async`/`await` native
- **Parameterized by default** — SQL injection is structurally prevented; raw string concatenation is never required
- **Pluggable logging** — bring `NullDbLogger`, `ConsoleDbLogger`, or wrap your own `ILogger`/`ILogger<T>` via `MicrosoftLoggerAdapter` (opt-in with `MYSQL_MEL_LOGGING`)
- **Fluent query builder** — `SelectBuilder` composes `WHERE`, `JOIN`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT`, and `OFFSET` clauses without string manipulation
- **Transaction helpers** — `BeginTransactionAsync`, `TransactAsync` callback, savepoints, and auto-rollback on dispose
- **Bulk insert** — single-shot, chunked, and transacted variants for large data loads
- **Schema management** — `EnsureTableAsync` creates or migrates tables; `EnsureIndexAsync` adds missing indexes idempotently
- **Streaming** — `QueryStreamAsync` returns `IAsyncEnumerable<DbRow>` for large result sets without buffering
- **`DbRow` result type** — typed `Get<T>`, `TryGet<T>`, and named helpers (`GetString`, `GetInt`, `GetLong`, etc.) with null safety
- **`Params.Of(...)` helpers** — concise parameter dictionary construction inline with queries

---

## Installation

Add `MySqlConnector` to your project and include `MySqlDatabase.cs` directly, or reference the project.

```bash
dotnet add package MySqlConnector
```

To enable `Microsoft.Extensions.Logging` adapters, define the `MYSQL_MEL_LOGGING` preprocessor symbol in your project file:

```xml
<DefineConstants>MYSQL_MEL_LOGGING</DefineConstants>
```

---

## Quick Start

```csharp
MySqlConnectionOptions options = new MySqlConnectionOptions
{
    Server   = "localhost",
    Database = "mydb",
    UserId   = "root",
    Password = "secret"
};

MySqlDatabase db = new MySqlDatabase(options, ConsoleDbLogger.Instance);

// Execute a non-query
await db.ExecuteAsync("UPDATE users SET active = 1 WHERE id = @id",Params.Of(("id", 42)));

// Query multiple rows
List<DbRow> rows = await db.QueryAsync("SELECT * FROM users WHERE role = @role", Params.Of(("role", "admin")));

foreach (DbRow row in rows)
    Console.WriteLine($"{row.GetString("username")} — {row.GetString("email")}");

// Query a single row
DbRow? user = await db.QuerySingleAsync("SELECT * FROM users WHERE id = @id", Params.Of(("id", 1)));

// Scalar value
long count = await db.ScalarAsync<long>("SELECT COUNT(*) FROM users") ?? 0;
```

---

## Logging

```csharp
// No logging (default)
MySqlDatabase db = new MySqlDatabase(connectionString);

// Console logging
MySqlDatabase db = new MySqlDatabase(connectionString, ConsoleDbLogger.Instance);
ConsoleDbLogger.Instance.MinLevel = DbLogLevel.Debug; // optional

// Microsoft.Extensions.Logging (requires MYSQL_MEL_LOGGING)
ILogger<MyService> msLogger = ...; // injected
MySqlDatabase db = new MySqlDatabase(connectionString, new MicrosoftLoggerAdapter<MyService>(msLogger));

// Custom logger
public class MyLogger : IDbLogger
{
    public void Log(DbLogLevel level, string message, Exception? ex = null) => MyLoggingSystem.Write(level.ToString(), message, ex);
}
```

---

## Fluent Query Builder

```csharp
List<DbRow> results = await new SelectBuilder()
    .From("orders")
    .Select("orders.id", "orders.total", "users.username")
    .Join("users", "users.id = orders.user_id")
    .Where("orders.status = @status", Params.Of(("status", "pending")))
    .Where("orders.total > @min",     Params.Of(("min", 100m)))
    .OrderBy("orders.total", descending: true)
    .Limit(50)
    .RunAsync(db);
```

---

## Inserts and Upserts

```csharp
// Insert
await db.InsertAsync("users", new Dictionary<string, object?>
{
    ["username"] = "alice",
    ["email"]    = "alice@example.com",
    ["active"]   = true
});

// Insert and get the auto-increment ID
long newId = await db.InsertGetIdAsync("users", new Dictionary<string, object?>
{
    ["username"] = "bob",
    ["email"]    = "bob@example.com"
});

// Upsert (INSERT … ON DUPLICATE KEY UPDATE)
await db.UpsertAsync("settings", new Dictionary<string, object?>
{
    ["key"]   = "theme",
    ["value"] = "dark"
});

// Update with WHERE
await db.UpdateAsync("users",
    setValues:   new Dictionary<string, object?> { ["active"] = false },
    where:       "last_login < @cutoff",
    whereParams: Params.Of(("cutoff", DateTime.UtcNow.AddYears(-1))));

// Delete
await db.DeleteAsync("sessions", "expires_at < @now", Params.Of(("now", DateTime.UtcNow)));
```

---

## Transactions

```csharp
// Callback style — commit and rollback handled automatically
await db.TransactAsync(async tx =>
{
    await tx.ExecuteAsync("INSERT INTO orders (user_id, total) VALUES (@uid, @total)", Params.Of(("uid", 5), ("total", 299.99m)));

    long orderId = await tx.ScalarAsync<long>("SELECT LAST_INSERT_ID()") ?? 0;

    await tx.ExecuteAsync("INSERT INTO order_items (order_id, sku) VALUES (@oid, @sku)", Params.Of(("oid", orderId), ("sku", "WIDGET-1")));
});

// Manual style with savepoints
await using MySqlDatabaseTransaction tx = await db.BeginTransactionAsync();
try
{
    await tx.SavepointAsync("before_risky_op");
    // ...
    await tx.RollbackToAsync("before_risky_op");
    // ...
    await tx.CommitAsync();
}
catch
{
    await tx.RollbackAsync();
    throw;
}
```

---

## Bulk Inserts

```csharp
List<Dictionary<string, object?>> rows = items.Select(i => new Dictionary<string, object?>
{
    ["sku"]      = i.Sku,
    ["quantity"] = i.Quantity,
    ["price"]    = i.Price
}).ToList();

// Single batch (small sets)
await db.BulkInsertAsync("inventory", rows);

// Chunked (large sets, configurable chunk size)
await db.BulkInsertChunkedAsync("inventory", rows, chunkSize: 1000);

// Chunked inside a single transaction (all-or-nothing)
await db.BulkInsertTransactedAsync("inventory", rows, chunkSize: 500);
```

---

## Schema Management

```csharp
// Fluent schema definition
TableSchema schema = new TableSchema("players")
    .PrimaryKey("id",       "BIGINT UNSIGNED NOT NULL AUTO_INCREMENT")
    .Column("username",     "VARCHAR(64) NOT NULL")
    .Column("email",        "VARCHAR(255) NOT NULL")
    .Column("created_at",   "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP")
    .UniqueIndex("uq_username", "username")
    .UniqueIndex("uq_email",    "email")
    .Index("idx_created",       "created_at");

// Creates the table if missing; adds any new columns if it already exists
await db.EnsureTableAsync(schema);

// Check existence
bool exists = await db.TableExistsAsync("players");

// Inspect columns
List<ColumnInfo> columns = await db.GetColumnsAsync("players");

// Add a missing index without errors if it already exists
await db.EnsureIndexAsync("players", "idx_created", ["created_at"]);
```

---

## Streaming Large Result Sets

```csharp
await foreach (DbRow row in db.QueryStreamAsync("SELECT * FROM event_log WHERE created_at > @since", Params.Of(("since", DateTime.UtcNow.AddDays(-7)))))
{
    ProcessEvent(row);
}
```

---

## Connection Options

| Property | Default | Description |
|---|---|---|
| `Server` | `localhost` | MySQL host |
| `Port` | `3306` | MySQL port |
| `Database` | _(required)_ | Database name |
| `UserId` | _(required)_ | Login username |
| `Password` | _(required)_ | Login password |
| `Pooling` | `true` | Enable connection pooling |
| `MinPoolSize` | `0` | Minimum pool connections |
| `MaxPoolSize` | `100` | Maximum pool connections |
| `ConnectTimeout` | `30` | Connection timeout in seconds |
| `AllowZeroDateTime` | `true` | Allow `0000-00-00` dates |
| `ConvertZeroDateTime` | `true` | Convert zero dates to `DateTime.MinValue` |
| `SslMode` | `Preferred` | SSL connection mode |

---

## DbRow API

| Method | Description |
|---|---|
| `Get<T>(column)` | Returns value cast to `T`, or `default` if null |
| `TryGet<T>(column, out value)` | Safe typed retrieval; returns `false` if null or cast fails |
| `GetOrDefault<T>(column, fallback)` | Returns value or a provided fallback |
| `GetString`, `GetInt`, `GetLong`, `GetFloat`, `GetDouble`, `GetDecimal`, `GetBool`, `GetDateTime`, `GetGuid`, `GetBytes` | Typed convenience accessors |
| `IsNull(column)` | Returns `true` if column is null or missing |
| `Columns` | List of column names in result order |
| `this[string]` / `this[int]` | Raw `object?` access by name or index |
| `ToDictionary()` | Copy of the underlying data as a dictionary |

---

## Requirements

- .NET 8 or later
- [MySqlConnector](https://www.nuget.org/packages/MySqlConnector) NuGet package

---

## License

Free for personal and commercial use. No warranty provided.
