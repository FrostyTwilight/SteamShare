using System.Text.Json;

using Microsoft.Data.Sqlite;

using Serilog;

using SteamShare.Core.Models;

namespace SteamShare.Core.Services;

/// <summary>
/// Persistence service for the tracking database (steamshare.db via SQLite).
/// Opens a connection per operation to avoid file-locking conflicts.
/// Auto-migrates legacy filegroups.json data on first load.
/// </summary>
public sealed class TrackingDatabaseService
{
    private static readonly ILogger LogSerilog = Log.ForContext<TrackingDatabaseService>();

    private readonly string _dataDirectory;
    private readonly string _dbPath;
    private readonly string _connectionString;

    /// <summary>
    /// Creates the service, initializes the SQLite database schema,
    /// and migrates legacy JSON data if present.
    /// </summary>
    /// <param name="dataDirectory">Directory where steamshare.db is stored.</param>
    public TrackingDatabaseService(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("Data directory must not be null or empty.", nameof(dataDirectory));
        }

        _dataDirectory = dataDirectory;

        if (!Directory.Exists(_dataDirectory))
        {
            Directory.CreateDirectory(_dataDirectory);
        }

        _dbPath = Path.Combine(_dataDirectory, "steamshare.db");
        _connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate;Pooling=False;Default Timeout=30";

        LogSerilog.Information("Initializing SQLite tracking database at {Path}", _dbPath);

        // Open a temp connection for initialization and migration, then close it
        InitializeAndMigrate();

        var count = GetCount();
        LogSerilog.Information("Tracking database ready: {Count} entries", count);
    }

    /// <summary>
    /// Returns all entries in the database.
    /// </summary>
    public IReadOnlyList<FileGroupTrackingEntry> GetAll()
    {
        var results = new List<FileGroupTrackingEntry>();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT published_file_id, local_path, state, cached_name, cached_visibility, last_synced_at FROM file_groups;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEntry(reader));
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Returns the entry for the given published file ID, or null if not found.
    /// </summary>
    public FileGroupTrackingEntry? GetByPublishedFileId(ulong id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT published_file_id, local_path, state, cached_name, cached_visibility, last_synced_at FROM file_groups WHERE published_file_id = @id;";
        cmd.Parameters.AddWithValue("@id", (long)id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadEntry(reader);
        }

        return null;
    }

    /// <summary>
    /// Returns all entries matching the given download state.
    /// </summary>
    public IReadOnlyList<FileGroupTrackingEntry> GetByState(DownloadState state)
    {
        var results = new List<FileGroupTrackingEntry>();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT published_file_id, local_path, state, cached_name, cached_visibility, last_synced_at FROM file_groups WHERE state = @state;";
        cmd.Parameters.AddWithValue("@state", (int)state);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEntry(reader));
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Adds or updates an entry in the database.
    /// Does NOT automatically flush to disk — call <see cref="SaveAsync"/> to persist.
    /// </summary>
    public void Upsert(FileGroupTrackingEntry entry)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO file_groups (published_file_id, local_path, state, cached_name, cached_visibility, last_synced_at)
            VALUES (@id, @localPath, @state, @cachedName, @cachedVisibility, @lastSyncedAt);
        ";
        cmd.Parameters.AddWithValue("@id", (long)entry.PublishedFileId);
        cmd.Parameters.AddWithValue("@localPath", (object?)entry.LocalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@state", (int)entry.State);
        cmd.Parameters.AddWithValue("@cachedName", entry.CachedName);
        cmd.Parameters.AddWithValue("@cachedVisibility", (int)entry.CachedVisibility);
        cmd.Parameters.AddWithValue("@lastSyncedAt", (object?)entry.LastSyncedAt?.ToString("O") ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes an entry by published file ID. Returns true if it was removed.
    /// </summary>
    public bool Remove(ulong publishedFileId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM file_groups WHERE published_file_id = @id;";
        cmd.Parameters.AddWithValue("@id", (long)publishedFileId);

        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Persists pending writes. In DELETE journal mode, all writes are
    /// committed immediately, so this is a no-op (with cancellation support).
    /// </summary>
    public Task SaveAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        LogSerilog.Debug("Tracking database save (no-op in DELETE journal mode)");
        return Task.CompletedTask;
    }

    // ── Pending tasks ─────────────────────────────────────────────

    /// <summary>
    /// Saves a pending task so it can survive application restarts.
    /// Unused columns are passed as null/0.
    /// </summary>
    public void SavePendingTask(int taskType, ulong? publishedFileId, string? shareKey,
        string? password, string? sourcePath, string? targetPath, string? name,
        string? virtualFolderPath, int visibility, string? taskId = null)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO pending_tasks (task_type, published_file_id, share_key, password,
                source_path, target_path, name, virtual_folder_path, visibility, task_id, created_at)
            VALUES (@taskType, @publishedFileId, @shareKey, @password,
                @sourcePath, @targetPath, @name, @virtualFolderPath, @visibility, @taskId, @createdAt);
        ";
        cmd.Parameters.AddWithValue("@taskType", taskType);
        cmd.Parameters.AddWithValue("@publishedFileId", (object?)publishedFileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@shareKey", (object?)shareKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@password", (object?)password ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sourcePath", (object?)sourcePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@targetPath", (object?)targetPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@virtualFolderPath", (object?)virtualFolderPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@visibility", visibility);
        cmd.Parameters.AddWithValue("@taskId", (object?)taskId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns all pending tasks, ordered by creation time (oldest first).
    /// </summary>
    public List<PendingTaskRecord> GetPendingTasks()
    {
        var results = new List<PendingTaskRecord>();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, task_type, published_file_id, share_key, password,
                source_path, target_path, name, virtual_folder_path, visibility, task_id, created_at
            FROM pending_tasks
            ORDER BY created_at;
        ";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadPendingTask(reader));
        }

        return results;
    }

    /// <summary>
    /// Deletes a single pending task by its ID.
    /// </summary>
    public void DeletePendingTask(long id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deletes all rows from the pending_tasks table.
    /// Used when auto-restart of pending tasks is disabled.
    /// </summary>
    public void ClearPendingTasks()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_tasks;";
        var removed = cmd.ExecuteNonQuery();
        if (removed > 0)
        {
            LogSerilog.Information("Cleared {Count} pending task(s) from tracking database", removed);
        }
    }

    /// <summary>
    /// Deletes all pending tasks associated with the given published file ID.
    /// Useful when a file group is removed or its upload completes.
    /// </summary>
    public void DeletePendingTasksByFileId(ulong publishedFileId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_tasks WHERE published_file_id = @publishedFileId;";
        cmd.Parameters.AddWithValue("@publishedFileId", (long)publishedFileId);

        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Removes duplicate rows from the pending_tasks table, keeping only
    /// the row with the highest <c>id</c> for each unique key.
    /// </summary>
    /// <remarks>
    /// Downloads are grouped by (<c>task_type = 0</c>, <c>share_key</c>).
    /// Uploads are grouped by (<c>task_type = 1</c>, <c>source_path</c>, <c>name</c>).
    /// Called on restart before pending tasks are enumerated so that
    /// re-enqueued duplicates do not stack up across sessions.
    /// </remarks>
    public void DeleteDuplicatePendingTasks()
    {
        try
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM pending_tasks WHERE id NOT IN (
                    SELECT MAX(id) FROM pending_tasks WHERE task_type = 0 GROUP BY share_key
                    UNION
                    SELECT MAX(id) FROM pending_tasks WHERE task_type = 1 GROUP BY source_path, name
                );
            ";
            var removed = cmd.ExecuteNonQuery();
            if (removed > 0)
            {
                LogSerilog.Information(
                    "Removed {Count} duplicate pending task(s) from the tracking database",
                    removed);
            }
        }
        catch (SqliteException ex)
        {
            LogSerilog.Error(ex, "Failed to delete duplicate pending tasks");
        }
    }

    // ── Private helpers ───────────────────────────────────────────

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private int GetCount()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM file_groups;";
        return Convert.ToInt32(cmd.ExecuteScalar()!);
    }

    private static void InitializeDatabase(SqliteConnection conn)
    {
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=DELETE;";
            pragmaCmd.ExecuteNonQuery();
        }

        using (var schemaCmd = conn.CreateCommand())
        {
            schemaCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS file_groups (
                    published_file_id INTEGER PRIMARY KEY,
                    local_path TEXT,
                    state INTEGER NOT NULL DEFAULT 0,
                    cached_name TEXT NOT NULL DEFAULT '',
                    cached_visibility INTEGER NOT NULL DEFAULT 0,
                    last_synced_at TEXT
                );

                CREATE TABLE IF NOT EXISTS pending_tasks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_type INTEGER NOT NULL,
                    published_file_id INTEGER,
                    share_key TEXT,
                    password TEXT,
                    source_path TEXT,
                    target_path TEXT,
                    name TEXT,
                    virtual_folder_path TEXT,
                    visibility INTEGER DEFAULT 0,
                    task_id TEXT,
                    created_at TEXT NOT NULL
                );
            ";
            schemaCmd.ExecuteNonQuery();
        }
    }

    private static void MigratePendingTasksSchema(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE pending_tasks ADD COLUMN task_id TEXT;";
            cmd.ExecuteNonQuery();
            LogSerilog.Information("Added task_id column to pending_tasks table");
        }
        catch (SqliteException)
        {
            // Column already exists — idempotent
        }
    }

    private void InitializeAndMigrate()
    {
        try
        {
            using var conn = OpenConnection();
            InitializeDatabase(conn);
            MigrateFromLegacyJson(conn);
            MigratePendingTasksSchema(conn);
        }
        catch (SqliteException ex)
        {
            // Database may be corrupted — delete and retry
            LogSerilog.Warning(ex, "Failed to initialize database at {Path}, recreating", _dbPath);

            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch (Exception deleteEx)
            {
                LogSerilog.Error(deleteEx, "Failed to delete corrupted database file");
                throw;
            }

            using var conn = OpenConnection();
            InitializeDatabase(conn);
            MigrateFromLegacyJson(conn);
            MigratePendingTasksSchema(conn);
        }
    }

    private static FileGroupTrackingEntry ReadEntry(SqliteDataReader reader)
    {
        return new FileGroupTrackingEntry
        {
            PublishedFileId = (ulong)reader.GetInt64(0),
            LocalPath = reader.IsDBNull(1) ? null : reader.GetString(1),
            State = (DownloadState)reader.GetInt32(2),
            CachedName = reader.GetString(3),
            CachedVisibility = (WorkshopVisibility)reader.GetInt32(4),
            LastSyncedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    private static PendingTaskRecord ReadPendingTask(SqliteDataReader reader)
    {
        return new PendingTaskRecord(
            Id: reader.GetInt64(0),
            TaskType: reader.GetInt32(1),
            PublishedFileId: reader.IsDBNull(2) ? null : (ulong)reader.GetInt64(2),
            ShareKey: reader.IsDBNull(3) ? null : reader.GetString(3),
            Password: reader.IsDBNull(4) ? null : reader.GetString(4),
            SourcePath: reader.IsDBNull(5) ? null : reader.GetString(5),
            TargetPath: reader.IsDBNull(6) ? null : reader.GetString(6),
            Name: reader.IsDBNull(7) ? null : reader.GetString(7),
            VirtualFolderPath: reader.IsDBNull(8) ? null : reader.GetString(8),
            Visibility: reader.GetInt32(9),
            TaskId: reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt: reader.GetString(11));
    }

    /// <summary>
    /// If legacy filegroups.json exists and the SQLite table is empty,
    /// loads all entries from JSON and inserts them into SQLite,
    /// then renames the JSON file to filegroups.json.bak.
    /// </summary>
    private void MigrateFromLegacyJson(SqliteConnection conn)
    {
        var jsonPath = Path.Combine(_dataDirectory, "filegroups.json");
        if (!File.Exists(jsonPath))
        {
            return;
        }

        // Check if table is already populated
        if (GetCount() > 0)
        {
            LogSerilog.Information("SQLite database already has data, skipping migration");
            return;
        }

        LogSerilog.Information("Found legacy filegroups.json, migrating to SQLite...");

        try
        {
            var json = File.ReadAllText(jsonPath);
            var database = JsonSerializer.Deserialize<TrackingDatabase>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (database?.Entries is { } entries)
            {
                foreach (var entry in entries)
                {
                    Upsert(entry);
                }

                LogSerilog.Information("Migrated {Count} entries from filegroups.json to SQLite", entries.Count);
            }

            // Rename legacy file to .bak
            var bakPath = jsonPath + ".bak";
            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }

            File.Move(jsonPath, bakPath);
            LogSerilog.Information("Renamed filegroups.json to filegroups.json.bak");
        }
        catch (JsonException ex)
        {
            LogSerilog.Warning(ex, "Corrupted legacy filegroups.json, skipping migration");
            // Rename the corrupted file so we don't retry every startup
            var bakPath = jsonPath + ".bak";
            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }

            File.Move(jsonPath, bakPath);
        }
    }
}

/// <summary>
/// Record representing a row in the pending_tasks table.
/// </summary>
public sealed record PendingTaskRecord(
    long Id,
    int TaskType,
    ulong? PublishedFileId,
    string? ShareKey,
    string? Password,
    string? SourcePath,
    string? TargetPath,
    string? Name,
    string? VirtualFolderPath,
    int Visibility,
    string? TaskId,
    string CreatedAt);
