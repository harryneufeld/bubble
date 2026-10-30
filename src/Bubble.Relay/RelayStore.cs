using System.Security.Cryptography;
using Bubble.Core;
using Microsoft.Data.Sqlite;

namespace Bubble.Relay;

/// <summary>
/// Stores nothing but encrypted blobs. Deliberately not stored:
/// IP addresses, receive times, user or device identifiers.
/// </summary>
public sealed class RelayStore
{
    readonly string _connectionString;
    readonly TimeProvider _time;

    public RelayStore(string databasePath, TimeProvider time)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        _time = time;
        using var db = Open();
        Execute(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS messages (
                seq        INTEGER PRIMARY KEY AUTOINCREMENT,
                mailbox    TEXT    NOT NULL,
                id         TEXT    NOT NULL,
                ciphertext BLOB    NOT NULL,
                expires_at INTEGER NULL,
                UNIQUE (mailbox, id)
            );
            CREATE INDEX IF NOT EXISTS ix_messages_mailbox ON messages (mailbox, seq);
            CREATE INDEX IF NOT EXISTS ix_messages_expiry ON messages (expires_at) WHERE expires_at IS NOT NULL;
            CREATE TABLE IF NOT EXISTS backups (
                backup_id  TEXT PRIMARY KEY,
                token_hash BLOB NOT NULL,
                ciphertext BLOB NOT NULL
            );
            """);
    }

    /// <summary>False if the message ID already exists in this mailbox.</summary>
    public bool AddMessage(string mailbox, string id, byte[] ciphertext, long? expiresAt)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO messages (mailbox, id, ciphertext, expires_at) VALUES ($m, $id, $c, $e)";
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", ciphertext);
        cmd.Parameters.AddWithValue("$e", (object?)expiresAt ?? DBNull.Value);
        return cmd.ExecuteNonQuery() == 1;
    }

    public List<RelayMessageOut> GetMessages(string mailbox, long after)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT seq, id, ciphertext, expires_at FROM messages
            WHERE mailbox = $m AND seq > $after AND (expires_at IS NULL OR expires_at > $now)
            ORDER BY seq LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$after", after);
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$limit", RelayLimits.PageSize);
        using var reader = cmd.ExecuteReader();
        var result = new List<RelayMessageOut>();
        while (reader.Read())
        {
            result.Add(new RelayMessageOut(
                reader.GetInt64(0),
                reader.GetString(1),
                Convert.ToBase64String((byte[])reader[2]),
                reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        }
        return result;
    }

    public int DeleteExpired()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE expires_at IS NOT NULL AND expires_at <= $now";
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToUnixTimeSeconds());
        return cmd.ExecuteNonQuery();
    }

    public enum BackupWrite { Created, Updated, Forbidden }

    /// <summary>The first write sets the token; after that only someone who knows it may write.</summary>
    public BackupWrite PutBackup(string backupId, string token, byte[] ciphertext)
    {
        var tokenHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        using var db = Open();
        using var tx = db.BeginTransaction();
        var existing = GetTokenHash(db, backupId);
        if (existing is not null && !CryptographicOperations.FixedTimeEquals(existing, tokenHash))
            return BackupWrite.Forbidden;

        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO backups (backup_id, token_hash, ciphertext) VALUES ($id, $t, $c)
            ON CONFLICT (backup_id) DO UPDATE SET ciphertext = excluded.ciphertext
            """;
        cmd.Parameters.AddWithValue("$id", backupId);
        cmd.Parameters.AddWithValue("$t", tokenHash);
        cmd.Parameters.AddWithValue("$c", ciphertext);
        cmd.ExecuteNonQuery();
        tx.Commit();
        return existing is null ? BackupWrite.Created : BackupWrite.Updated;
    }

    /// <summary>Null if there is no backup or the token does not match (both look the same to the client).</summary>
    public byte[]? GetBackup(string backupId, string token)
    {
        var tokenHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT token_hash, ciphertext FROM backups WHERE backup_id = $id";
        cmd.Parameters.AddWithValue("$id", backupId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || !CryptographicOperations.FixedTimeEquals((byte[])reader[0], tokenHash))
            return null;
        return (byte[])reader[1];
    }

    static byte[]? GetTokenHash(SqliteConnection db, string backupId)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT token_hash FROM backups WHERE backup_id = $id";
        cmd.Parameters.AddWithValue("$id", backupId);
        return cmd.ExecuteScalar() as byte[];
    }

    SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    static void Execute(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Regularly deletes expired 24h posts for good.</summary>
public sealed class ExpiryCleanup(RelayStore store, ILogger<ExpiryCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            var deleted = store.DeleteExpired();
            if (deleted > 0) logger.LogInformation("{Count} expired messages deleted", deleted);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
