using Npgsql;

namespace MusicServer.Services;

public sealed class UserDbService
{
    private readonly NpgsqlDataSource _ds;

    public UserDbService(IConfiguration cfg)
    {
        var connStr = cfg.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres no está configurado en appsettings.json");
        _ds = NpgsqlDataSource.Create(connStr);
        InitDb();
    }

    private void InitDb()
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id            TEXT PRIMARY KEY,
                username      TEXT UNIQUE NOT NULL,
                password_hash TEXT NOT NULL,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS favorites (
                user_id    TEXT NOT NULL,
                track_id   TEXT NOT NULL,
                added_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (user_id, track_id)
            );
            CREATE TABLE IF NOT EXISTS playlists (
                id         TEXT PRIMARY KEY,
                user_id    TEXT NOT NULL,
                name       TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS playlist_tracks (
                playlist_id TEXT    NOT NULL,
                track_id    TEXT    NOT NULL,
                position    INTEGER NOT NULL,
                added_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (playlist_id, track_id)
            );
            CREATE TABLE IF NOT EXISTS play_history (
                id         BIGSERIAL PRIMARY KEY,
                user_id    TEXT NOT NULL,
                track_id   TEXT NOT NULL,
                played_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_ph_user  ON play_history(user_id);
            CREATE INDEX IF NOT EXISTS idx_pl_user  ON playlists(user_id);
            CREATE INDEX IF NOT EXISTS idx_fav_user ON favorites(user_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Users ────────────────────────────────────────────────────────────────

    public bool TryCreateUser(string username, string passwordHash, out string userId)
    {
        userId = Guid.NewGuid().ToString("N");
        try
        {
            using var conn = _ds.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO users (id, username, password_hash) VALUES (@id, @u, @h)";
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("u", username.ToLowerInvariant());
            cmd.Parameters.AddWithValue("h", passwordHash);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation
        {
            userId = "";
            return false;
        }
    }

    public (string Id, string PasswordHash)? FindUser(string username)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, password_hash FROM users WHERE username = @u";
        cmd.Parameters.AddWithValue("u", username.ToLowerInvariant());
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : null;
    }

    // ── Favorites ────────────────────────────────────────────────────────────

    public List<string> GetFavorites(string userId)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT track_id FROM favorites WHERE user_id = @uid ORDER BY added_at DESC";
        cmd.Parameters.AddWithValue("uid", userId);
        using var r = cmd.ExecuteReader();
        var result = new List<string>();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public void AddFavorite(string userId, string trackId)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO favorites (user_id, track_id) VALUES (@uid, @tid) ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tid", trackId);
        cmd.ExecuteNonQuery();
    }

    public void RemoveFavorite(string userId, string trackId)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM favorites WHERE user_id = @uid AND track_id = @tid";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tid", trackId);
        cmd.ExecuteNonQuery();
    }

    // ── Playlists ────────────────────────────────────────────────────────────

    public List<PlaylistDto> GetPlaylists(string userId)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, created_at FROM playlists WHERE user_id = @uid ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("uid", userId);
        using var r = cmd.ExecuteReader();
        var result = new List<PlaylistDto>();
        while (r.Read())
            result.Add(new PlaylistDto(r.GetString(0), r.GetString(1), r.GetDateTime(2)));
        return result;
    }

    public PlaylistDto CreatePlaylist(string userId, string name)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO playlists (id, user_id, name) VALUES (@id, @uid, @name) RETURNING created_at";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("name", name);
        var createdAt = (DateTime)cmd.ExecuteScalar()!;
        return new PlaylistDto(id, name, createdAt);
    }

    public bool DeletePlaylist(string userId, string playlistId)
    {
        using var conn = _ds.OpenConnection();
        using var tx = conn.BeginTransaction();

        using var del1 = conn.CreateCommand();
        del1.Transaction = tx;
        del1.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = @pid";
        del1.Parameters.AddWithValue("pid", playlistId);
        del1.ExecuteNonQuery();

        using var del2 = conn.CreateCommand();
        del2.Transaction = tx;
        del2.CommandText = "DELETE FROM playlists WHERE id = @pid AND user_id = @uid";
        del2.Parameters.AddWithValue("pid", playlistId);
        del2.Parameters.AddWithValue("uid", userId);
        var rows = del2.ExecuteNonQuery();

        tx.Commit();
        return rows > 0;
    }

    public List<string>? GetPlaylistTracks(string userId, string playlistId)
    {
        using var conn = _ds.OpenConnection();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT 1 FROM playlists WHERE id = @pid AND user_id = @uid";
        verify.Parameters.AddWithValue("pid", playlistId);
        verify.Parameters.AddWithValue("uid", userId);
        if (verify.ExecuteScalar() is null) return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT track_id FROM playlist_tracks WHERE playlist_id = @pid ORDER BY position";
        cmd.Parameters.AddWithValue("pid", playlistId);
        using var r = cmd.ExecuteReader();
        var result = new List<string>();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    public bool AddTrackToPlaylist(string userId, string playlistId, string trackId)
    {
        using var conn = _ds.OpenConnection();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT 1 FROM playlists WHERE id = @pid AND user_id = @uid";
        verify.Parameters.AddWithValue("pid", playlistId);
        verify.Parameters.AddWithValue("uid", userId);
        if (verify.ExecuteScalar() is null) return false;

        using var maxPos = conn.CreateCommand();
        maxPos.CommandText = "SELECT COALESCE(MAX(position), 0) FROM playlist_tracks WHERE playlist_id = @pid";
        maxPos.Parameters.AddWithValue("pid", playlistId);
        var pos = Convert.ToInt32(maxPos.ExecuteScalar()) + 1;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO playlist_tracks (playlist_id, track_id, position) VALUES (@pid, @tid, @pos) ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("pid", playlistId);
        cmd.Parameters.AddWithValue("tid", trackId);
        cmd.Parameters.AddWithValue("pos", pos);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void RemoveTrackFromPlaylist(string userId, string playlistId, string trackId)
    {
        using var conn = _ds.OpenConnection();

        using var verify = conn.CreateCommand();
        verify.CommandText = "SELECT 1 FROM playlists WHERE id = @pid AND user_id = @uid";
        verify.Parameters.AddWithValue("pid", playlistId);
        verify.Parameters.AddWithValue("uid", userId);
        if (verify.ExecuteScalar() is null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playlist_tracks WHERE playlist_id = @pid AND track_id = @tid";
        cmd.Parameters.AddWithValue("pid", playlistId);
        cmd.Parameters.AddWithValue("tid", trackId);
        cmd.ExecuteNonQuery();
    }

    // ── Play history ─────────────────────────────────────────────────────────

    public void RecordPlay(string userId, string trackId)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO play_history (user_id, track_id) VALUES (@uid, @tid)";
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("tid", trackId);
        cmd.ExecuteNonQuery();
    }

    public List<(string TrackId, int Count)> GetMostPlayed(string userId, int limit = 20)
    {
        using var conn = _ds.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT track_id, COUNT(*)::int AS play_count
            FROM play_history
            WHERE user_id = @uid
            GROUP BY track_id
            ORDER BY play_count DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("uid", userId);
        cmd.Parameters.AddWithValue("limit", limit);
        using var r = cmd.ExecuteReader();
        var result = new List<(string, int)>();
        while (r.Read()) result.Add((r.GetString(0), r.GetInt32(1)));
        return result;
    }

    public record PlaylistDto(string Id, string Name, DateTime CreatedAt);
}
