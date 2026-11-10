using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Notification.Infrastructure.Persistence;
using Npgsql;

const string Legacy = "20261026105458_InitialNotificationSchema";
const string Initial = "20261031002420_InitialCreate";
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var config = Option("--config");
var raw = config is null ? Environment.GetEnvironmentVariable("NOTIFICATION_UPGRADE_CONNECTION") :
    JsonDocument.Parse(File.ReadAllText(config)).RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("A configuration file or upgrade connection environment variable is required.");
NpgsqlConnectionStringBuilder builder;
if (raw.StartsWith("postgres://") || raw.StartsWith("postgresql://"))
{
    var uri = new Uri(raw); var info = uri.UserInfo.Split(':', 2);
    builder = new() { Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432, Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')), Username = Uri.UnescapeDataString(info[0]), Password = Uri.UnescapeDataString(info[1]) };
}
else builder = new(raw);
var local = args.Contains("--allow-local") && builder.Host is "127.0.0.1" or "localhost";
if (!local && builder.Host?.EndsWith(".neon.tech", StringComparison.OrdinalIgnoreCase) != true) throw new InvalidOperationException("Only Neon or an explicitly allowed local clone is supported.");
if (!local) builder.SslMode = SslMode.VerifyFull;
builder.Timeout = 15; builder.CommandTimeout = 120;
try { await Run(); }
catch (Exception error) { Console.Error.WriteLine(string.IsNullOrEmpty(builder.Password) ? error.Message : error.Message.Replace(builder.Password, "[redacted]")); Environment.ExitCode = 1; }

async Task Run()
{
    await using var connection = new NpgsqlConnection(builder.ConnectionString); await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    var assembly = db.GetService<IMigrationsAssembly>();
    var ids = assembly.Migrations.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var generator = db.GetService<IMigrationsSqlGenerator>(); var model = db.GetService<IDesignTimeModel>().Model;
    await using var tx = await connection.BeginTransactionAsync();
    async Task<int> Execute(string sql) { await using var c = new NpgsqlCommand(sql, connection, tx); return await c.ExecuteNonQueryAsync(); }
    async Task<object?> Scalar(string sql) { await using var c = new NpgsqlCommand(sql, connection, tx); return await c.ExecuteScalarAsync(); }
    async Task<List<string[]>> Rows(string sql) { await using var c = new NpgsqlCommand(sql, connection, tx); await using var r = await c.ExecuteReaderAsync(); var rows = new List<string[]>(); while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString()!).ToArray()); return rows; }
    async Task Operation(MigrationOperation op) { foreach (var cmd in generator.Generate([op], model)) { if (cmd.TransactionSuppressed) throw new InvalidOperationException("Nontransactional upgrade operation is not permitted."); await Execute(cmd.CommandText); } }
    await Execute("SET LOCAL lock_timeout='5s';SET LOCAL statement_timeout='120s';SET LOCAL TIME ZONE 'UTC';SELECT pg_advisory_xact_lock(hashtextextended('notification-legacy-upgrade',0));");
    var applied = (await Rows("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY 1")).Select(r => r[0]).ToHashSet();
    if (ids.All(applied.Contains)) { await CheckModel(); Console.WriteLine("AlreadyCurrent=true;NoChanges=true"); await tx.RollbackAsync(); return; }
    if (applied.Count != 1 || !applied.Contains(Legacy)) throw new InvalidOperationException("Expected the recognized legacy-only migration history;partial or unrelated histories require review.");
    if (args.Contains("--apply"))
    {
        var backup = Option("--backup-file") ?? throw new InvalidOperationException("Applying an upgrade requires the verified backup file.");
        var bytes = File.ReadAllBytes(backup); if (bytes.Length < 100 || Encoding.ASCII.GetString(bytes, 0, 5) != "PGDMP") throw new InvalidOperationException("A PostgreSQL custom-format backup is required.");
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(backup)!, "backup-manifest.json"))).RootElement;
        if (!string.Equals(manifest.GetProperty("DumpSha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Backup digest does not match its manifest.");
        var target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{builder.Host}:{builder.Port}/{builder.Database}")));
        if (!local && manifest.GetProperty("TargetFingerprint").GetString() != target) throw new InvalidOperationException("Backup does not match the selected Neon target.");
    }
    var tables = await Rows("SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE' ORDER BY 1");
    await Execute("LOCK TABLE " + string.Join(",", tables.Select(t => "public." + Q(t[0]))) + " IN ACCESS EXCLUSIVE MODE;");
    var columns = await Columns();
    var enums = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
    foreach (var entity in model.GetEntityTypes())
    {
        var table = entity.GetTableName(); if (table is null) continue;
        foreach (var p in entity.GetProperties())
        {
            var type = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType; if (!type.IsEnum) continue;
            var name = p.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema())); if (name is null) continue;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in Enum.GetValues(type)) { var number = Convert.ToInt32(value, CultureInfo.InvariantCulture); map[value.ToString()!] = number; map[number.ToString(CultureInfo.InvariantCulture)] = number; }
            enums[table + "." + name] = map;
        }
    }
    var initial = assembly.CreateMigration(assembly.Migrations[Initial], db.Database.ProviderName!);
    var creates = initial.UpOperations.OfType<CreateTableOperation>().ToArray();
    if (creates.Any(t => !columns.ContainsKey(t.Name))) throw new InvalidOperationException("Legacy core tables are missing;this transition does not guess a partial baseline.");
    // All enum values are checked before changing any column or relationship.
    foreach (var table in creates) foreach (var column in table.Columns)
    {
        if (!columns[table.Name].TryGetValue(column.Name, out var old) || old.Type == Normalize(column.ColumnType!)) continue;
        if (!enums.TryGetValue(table.Name + "." + column.Name, out var map) || column.ColumnType != "integer") throw new InvalidOperationException($"Unreviewed type transition: {table.Name}.{column.Name}.");
        foreach (var row in await Rows($"SELECT DISTINCT {Q(column.Name)}::text FROM {Q(table.Name)} WHERE {Q(column.Name)} IS NOT NULL")) if (!map.ContainsKey(row[0])) throw new InvalidOperationException($"Unmapped legacy enum at {table.Name}.{column.Name};no changes committed.");
    }
    var before = await Fingerprints();
    var fks = await Rows("SELECT t.relname,c.conname,pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE c.contype='f' AND n.nspname='public' ORDER BY t.relname,c.conname");
    var core = creates.Select(t => t.Name).ToHashSet();
    var detached = fks.Where(f => core.Contains(f[0])).ToArray();
    foreach (var fk in detached) await Execute($"ALTER TABLE {Q(fk[0])} DROP CONSTRAINT {Q(fk[1])};");
    foreach (var table in creates) foreach (var column in table.Columns)
    {
        var qualified = Q(table.Name); var quoted = Q(column.Name);
        if (!columns[table.Name].TryGetValue(column.Name, out var old))
        {
            if (!column.IsNullable && column.DefaultValue is null && column.DefaultValueSql is null && Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {qualified}")) > 0) throw new InvalidOperationException($"Required missing field {table.Name}.{column.Name} needs an explicit backfill.");
            await Operation(new AddColumnOperation { Name = column.Name, Table = table.Name, ClrType = column.ClrType, ColumnType = column.ColumnType, IsNullable = column.IsNullable, MaxLength = column.MaxLength, DefaultValue = column.DefaultValue, DefaultValueSql = column.DefaultValueSql });
            Console.WriteLine($"AddedColumn={table.Name}.{column.Name}"); continue;
        }
        if (old.Type != Normalize(column.ColumnType!))
        {
            if (!string.IsNullOrEmpty(old.Default)) throw new InvalidOperationException($"Enum default at {table.Name}.{column.Name} needs an explicit conversion.");
            var map = enums[table.Name + "." + column.Name];
            await Execute($"ALTER TABLE {qualified} ALTER COLUMN {quoted} TYPE integer USING ({Case(quoted, map)})::integer;");
            Console.WriteLine($"ConvertedEnum={table.Name}.{column.Name}");
        }
        if (old.Nullable != column.IsNullable)
        {
            if (!column.IsNullable && Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {qualified} WHERE {quoted} IS NULL")) > 0) throw new InvalidOperationException($"Null legacy values at {table.Name}.{column.Name} require review.");
            await Execute($"ALTER TABLE {qualified} ALTER COLUMN {quoted} {(column.IsNullable ? "DROP" : "SET")} NOT NULL;");
        }
    }
    foreach (var fk in detached) await Execute($"ALTER TABLE {Q(fk[0])} ADD CONSTRAINT {Q(fk[1])} {fk[2]};");
    foreach (var table in creates) foreach (var fk in table.ForeignKeys) if (!await ExistsConstraint(table.Name, fk.Name)) await Operation(fk);
    foreach (var op in initial.UpOperations.OfType<CreateIndexOperation>())
    {
        var existing = await Scalar($"SELECT to_regclass('public.{Q(op.Name)}')::text"); if (existing is null || existing is DBNull) await Operation(op);
    }
    // Preserve every old row. Add only required catalog seeds,never sample users/devices/messages/audits.
    foreach (var seed in initial.UpOperations.OfType<InsertDataOperation>().Where(s => s.Table is "NotificationChannels" or "NotificationTemplates")) for (var row = 0; row < seed.Values.GetLength(0); row++)
    {
        var idIndex = Array.IndexOf(seed.Columns, "Id"); var id = seed.Values[row, idIndex]!.ToString()!;
        var natural = seed.Table == "NotificationChannels" ? new[] { "Code" } : new[] { "Code", "Locale" };
        var predicate = string.Join(" AND ", natural.Select(name => Q(name) + "=" + Literal(seed.Values[row, Array.IndexOf(seed.Columns, name)])));
        var byId = Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {Q(seed.Table)} WHERE \"Id\"='{id}'"));
        var matching = Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {Q(seed.Table)} WHERE \"Id\"='{id}' AND {predicate}"));
        if (byId > 0 && matching != 1) throw new InvalidOperationException("Catalog seed identity conflicts with legacy data.");
        if (byId > 0) continue;
        if (Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM {Q(seed.Table)} WHERE {predicate}")) > 0) throw new InvalidOperationException("Catalog seed natural key exists with a different identity.");
        var values = new object[1, seed.Columns.Length]; for (var i = 0; i < seed.Columns.Length; i++) values[0, i] = seed.Values[row, i]!;
        await Operation(new InsertDataOperation { Table = seed.Table, Columns = seed.Columns, Values = values });
    }
    var version = model.GetProductVersion() ?? "8.0.0";
    await Execute($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES ('{Initial}','{version}');");
    foreach (var id in ids.Where(id => string.CompareOrdinal(id, Initial) > 0))
    {
        var migration = assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
        if (migration.UpOperations.Any(o => o is DropTableOperation or DropColumnOperation or DeleteDataOperation)) throw new InvalidOperationException("A destructive forward migration requires separate review.");
    }
    var script = db.GetService<IMigrator>().GenerateScript(Initial, ids[^1], MigrationsSqlGenerationOptions.NoTransactions);
    if (script.Contains("CONCURRENTLY", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Nontransactional indexes are not supported by this transition.");
    await Execute(script);
    await CheckModel();
    var after = await Fingerprints();
    foreach (var table in before) foreach (var row in table.Value) if (!after[table.Key].TryGetValue(row.Key, out var hash) || hash != row.Value) throw new InvalidOperationException($"Original data fingerprint changed in {table.Key};upgrade rolled back.");
    var history = (await Rows("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"")).Select(r => r[0]).ToHashSet();
    if (!ids.All(history.Contains) || !history.Contains(Legacy)) throw new InvalidOperationException("Migration history verification failed.");
    Console.WriteLine($"OriginalRowsVerified={before.Values.Sum(t => t.Count)};CurrentMigrations={ids.Length};LegacyHistoryRetained=true;SchemaVerified=true");
    if (args.Contains("--apply")) { await tx.CommitAsync(); Console.WriteLine("UpgradeCommitted=true"); }
    else { await tx.RollbackAsync(); Console.WriteLine("DryRunRolledBack=true;NoChanges=true"); }

    async Task<bool> ExistsConstraint(string table, string name) => Convert.ToInt64(await Scalar($"SELECT COUNT(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid WHERE t.relname={Literal(table)} AND c.conname={Literal(name)}")) > 0;
    async Task<Dictionary<string, Dictionary<string, Column>>> Columns()
    {
        var result = new Dictionary<string, Dictionary<string, Column>>();
        foreach (var r in await Rows("SELECT t.relname,a.attname,format_type(a.atttypid,a.atttypmod),a.attnotnull::text,coalesce(pg_get_expr(d.adbin,d.adrelid),'') FROM pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace JOIN pg_attribute a ON a.attrelid=t.oid LEFT JOIN pg_attrdef d ON d.adrelid=t.oid AND d.adnum=a.attnum WHERE n.nspname='public' AND t.relkind='r' AND a.attnum>0 AND NOT a.attisdropped ORDER BY t.relname,a.attnum"))
        {
            if (!result.TryGetValue(r[0], out var table)) result[r[0]] = table = new(); table[r[1]] = new(Normalize(r[2]), r[3] != "true", r[4]);
        }
        return result;
    }
    async Task<Dictionary<string, Dictionary<string, string>>> Fingerprints()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var table in tables.Select(t => t[0]))
        {
            var keys = await Rows($"SELECT a.attname FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace JOIN unnest(i.indkey) WITH ORDINALITY k(attnum,ord) ON true JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum WHERE n.nspname='public' AND t.relname={Literal(table)} AND i.indisprimary ORDER BY k.ord");
            if (keys.Count == 0) throw new InvalidOperationException("A legacy table has no stable primary key.");
            var key = "jsonb_build_array(" + string.Join(",", keys.Select(k => Q(k[0]) + "::text")) + ")::text";
            var expressions = columns[table].Keys.Where(c => !(table == "NotificationTemplates" && c == "IsMandatory")).Select(c => enums.TryGetValue(table + "." + c, out var map) ? "(" + Case(Q(c), map) + ")::text" : Q(c) + "::text");
            var hash = "md5(jsonb_build_array(" + string.Join(",", expressions) + ")::text)";
            result[table] = (await Rows($"SELECT {key},{hash} FROM {Q(table)}")).ToDictionary(r => r[0], r => r[1]);
        }
        return result;
    }
    async Task CheckModel()
    {
        var actual = await Columns();
        foreach (var table in model.GetRelationalModel().Tables) foreach (var col in table.Columns)
        {
            if (!actual.TryGetValue(table.Name, out var cols) || !cols.TryGetValue(col.Name, out var got) || got.Type != Normalize(col.StoreType) || got.Nullable != col.IsNullable) throw new InvalidOperationException($"Physical model mismatch at {table.Name}.{col.Name}.");
        }
    }
}
static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
static string Normalize(string value) => value.Replace(" ", "").ToLowerInvariant();
static string Literal(object? value) => value is null ? "NULL" : value is bool b ? (b ? "TRUE" : "FALSE") : value is int or long ? Convert.ToString(value, CultureInfo.InvariantCulture)! : "'" + value.ToString()!.Replace("'", "''") + "'";
static string Case(string column, Dictionary<string, int> map) => "CASE WHEN " + column + " IS NULL THEN NULL " + string.Join(" ", map.Select(p => "WHEN lower(" + column + "::text)=" + Literal(p.Key.ToLowerInvariant()) + " THEN " + Literal(p.Value.ToString(CultureInfo.InvariantCulture)))) + " ELSE " + column + "::text END";
sealed record Column(string Type, bool Nullable, string Default);
