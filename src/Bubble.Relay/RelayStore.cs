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
            CREATE TABLE IF NOT EXISTS push_subscriptions (
                id          TEXT PRIMARY KEY,
                secret_hash BLOB    NOT NULL,
                type        TEXT    NOT NULL,
                endpoint    TEXT    NOT NULL,
                last_seen   INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS push_mailboxes (
                subscription_id TEXT NOT NULL REFERENCES push_subscriptions (id) ON DELETE CASCADE,
                mailbox         TEXT NOT NULL,
                PRIMARY KEY (subscription_id, mailbox)
            );
            CREATE INDEX IF NOT EXISTS ix_push_mailboxes_mailbox ON push_mailboxes (mailbox);
            """);
        AddColumn(db, "messages", "secret_hash", "BLOB NULL");
        AddColumn(db, "messages", "parent_id", "TEXT NULL");
        Execute(db, "CREATE INDEX IF NOT EXISTS ix_messages_parent ON messages (mailbox, parent_id) WHERE parent_id IS NOT NULL;");
    }

    static void AddColumn(SqliteConnection db, string table, string column, string definition)
    {
        using var info = db.CreateCommand();
        info.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if (info.ExecuteScalar() is null) Execute(db, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    /// <summary>False if the message ID already exists in this mailbox.</summary>
    public bool AddMessage(string mailbox, string id, byte[] ciphertext, long? expiresAt, byte[]? secretHash = null, string? parentId = null)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO messages (mailbox, id, ciphertext, expires_at, secret_hash, parent_id)
            VALUES ($m, $id, $c, $e, $s, $p)
            """;
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", ciphertext);
        cmd.Parameters.AddWithValue("$e", (object?)expiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", (object?)secretHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
        return cmd.ExecuteNonQuery() == 1;
    }

    public enum MessageEdit { Done, NotFound, Forbidden }

    /// <summary>Sets a new expiry (null = permanent) on a message and on the hearts and comments that belong to it.</summary>
    public MessageEdit SetExpiry(string mailbox, string id, string secret, long? expiresAt)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (CheckSecret(db, mailbox, id, secret) is { } failure) return failure;
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE messages SET expires_at = $e WHERE mailbox = $m AND (id = $id OR parent_id = $id)";
        cmd.Parameters.AddWithValue("$e", (object?)expiresAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
        return MessageEdit.Done;
    }

    /// <summary>Deletes a message together with its hearts and comments.</summary>
    public MessageEdit DeleteMessage(string mailbox, string id, string secret)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (CheckSecret(db, mailbox, id, secret) is { } failure) return failure;
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM messages WHERE mailbox = $m AND (id = $id OR parent_id = $id)";
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
        return MessageEdit.Done;
    }

    /// <summary>Null if the secret belongs to the message. Messages stored without a secret hash can never be changed.</summary>
    static MessageEdit? CheckSecret(SqliteConnection db, string mailbox, string id, string secret)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT secret_hash FROM messages WHERE mailbox = $m AND id = $id";
        cmd.Parameters.AddWithValue("$m", mailbox);
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return MessageEdit.NotFound;
        if (reader.IsDBNull(0)) return MessageEdit.Forbidden;
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        return CryptographicOperations.FixedTimeEquals((byte[])reader[0], hash) ? null : MessageEdit.Forbidden;
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

    public PushSubscriptionOut CreateSubscription(string type, string endpoint, IEnumerable<string> mailboxes)
    {
        var id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
        var secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        using var db = Open();
        using var tx = db.BeginTransaction();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO push_subscriptions (id, secret_hash, type, endpoint, last_seen) VALUES ($id, $s, $t, $e, $seen)";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$s", HashSecret(secret));
            cmd.Parameters.AddWithValue("$t", type);
            cmd.Parameters.AddWithValue("$e", endpoint);
            cmd.Parameters.AddWithValue("$seen", Today());
            cmd.ExecuteNonQuery();
        }
        SetMailboxes(db, id, mailboxes);
        tx.Commit();
        return new PushSubscriptionOut(id, secret);
    }

    /// <summary>False if the subscription does not exist or the secret does not match (both look the same to the client).</summary>
    public bool UpdateSubscriptionMailboxes(string id, string secret, IEnumerable<string> mailboxes, string? newEndpoint = null)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!SecretMatches(db, id, secret)) return false;
        SetMailboxes(db, id, mailboxes);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE push_subscriptions SET last_seen = $seen, endpoint = COALESCE($e, endpoint) WHERE id = $id";
        cmd.Parameters.AddWithValue("$seen", Today());
        cmd.Parameters.AddWithValue("$e", (object?)newEndpoint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        tx.Commit();
        return true;
    }

    /// <summary>The subscription's type, or null if it does not exist or the secret does not match.</summary>
    public string? GetSubscriptionType(string id, string secret)
    {
        using var db = Open();
        if (!SecretMatches(db, id, secret)) return null;
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT type FROM push_subscriptions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() as string;
    }

    public bool DeleteSubscription(string id, string secret)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        if (!SecretMatches(db, id, secret)) return false;
        DeleteSubscription(db, id);
        tx.Commit();
        return true;
    }

    /// <summary>Called when the push endpoint reports the device as gone.</summary>
    public void DeleteSubscription(string id)
    {
        using var db = Open();
        DeleteSubscription(db, id);
    }

    public List<PushTarget> GetSubscriptionsForMailbox(string mailbox)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.type, s.endpoint FROM push_subscriptions s
            JOIN push_mailboxes m ON m.subscription_id = s.id
            WHERE m.mailbox = $m
            """;
        cmd.Parameters.AddWithValue("$m", mailbox);
        using var reader = cmd.ExecuteReader();
        var result = new List<PushTarget>();
        while (reader.Read())
            result.Add(new PushTarget(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public int DeleteStaleSubscriptions()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM push_subscriptions WHERE last_seen < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", Today() - (long)RelayLimits.PushSubscriptionTtl.TotalSeconds);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Only the day is kept, not the exact time.</summary>
    long Today() => _time.GetUtcNow().ToUnixTimeSeconds() / 86400 * 86400;

    static byte[] HashSecret(string secret) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));

    static bool SecretMatches(SqliteConnection db, string id, string secret)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT secret_hash FROM push_subscriptions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() is byte[] hash && CryptographicOperations.FixedTimeEquals(hash, HashSecret(secret));
    }

    static void SetMailboxes(SqliteConnection db, string id, IEnumerable<string> mailboxes)
    {
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM push_mailboxes WHERE subscription_id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        foreach (var mailbox in mailboxes.Distinct())
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO push_mailboxes (subscription_id, mailbox) VALUES ($id, $m)";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$m", mailbox);
            cmd.ExecuteNonQuery();
        }
    }

    static void DeleteSubscription(SqliteConnection db, string id)
    {
        // Foreign keys are off by default in SQLite, so delete the mailbox rows explicitly.
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            DELETE FROM push_mailboxes WHERE subscription_id = $id;
            DELETE FROM push_subscriptions WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
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

public sealed record PushTarget(string Id, string Type, string Endpoint);

/// <summary>Regularly deletes expired 24h posts and push subscriptions that were not renewed for good.</summary>
public sealed class ExpiryCleanup(RelayStore store, ILogger<ExpiryCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            var deleted = store.DeleteExpired();
            if (deleted > 0) logger.LogInformation("{Count} expired messages deleted", deleted);
            var stale = store.DeleteStaleSubscriptions();
            if (stale > 0) logger.LogInformation("{Count} stale push subscriptions deleted", stale);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
