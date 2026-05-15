using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace MusicServer.Services;

public sealed class MusicLibrary
{
    private readonly IConfiguration _cfg;
    private readonly FileExtensionContentTypeProvider _types = new();
    private volatile Snapshot _snap = new([], [], [], new());

    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".wav" };

    private string Root => _cfg["Music:RootPath"] ?? "";

    public MusicLibrary(IConfiguration cfg)
    {
        _cfg = cfg;
        _types.Mappings.TryAdd(".flac", "audio/flac");
        _types.Mappings.TryAdd(".m4a", "audio/mp4");
        _types.Mappings.TryAdd(".aac", "audio/aac");
        _types.Mappings.TryAdd(".ogg", "audio/ogg");
        _types.Mappings.TryAdd(".wav", "audio/wav");
    }

    public Snapshot Current => _snap;

    public void Rescan()
    {
        var root = Root;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException($"Music:RootPath inválido: {root}");

        var artists = new Dictionary<string, ArtistDto>();
        var albums  = new Dictionary<string, AlbumDto>();
        var tracks  = new Dictionary<string, TrackDto>();
        var pathByTrackId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var fullPath in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(fullPath);
            if (!AllowedExt.Contains(ext)) continue;

            var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // esperado: Artista/Album/archivo
            var artistName = parts.Length >= 3 ? parts[0] : "Unknown Artist";
            var albumName  = parts.Length >= 3 ? parts[1] : "Unknown Album";

            var artistId = HashHex("artist:" + artistName);
            var albumId  = HashHex("album:" + artistId + "/" + albumName);
            var trackId  = HashHex("track:" + rel);

            artists.TryAdd(artistId, new ArtistDto(artistId, artistName));
            albums.TryAdd(albumId, new AlbumDto(albumId, albumName, artistId));

            tracks[trackId] = new TrackDto(
                Id: trackId,
                Title: Path.GetFileNameWithoutExtension(fullPath),
                Ext: ext.ToLowerInvariant(),
                ArtistId: artistId,
                ArtistName: artistName,
                AlbumId: albumId,
                AlbumName: albumName
            );

            pathByTrackId[trackId] = fullPath;
        }

        _snap = new Snapshot(
            Artists: artists.Values.OrderBy(a => a.Name).ToArray(),
            Albums: albums.Values.OrderBy(a => a.Name).ToArray(),
            Tracks: tracks.Values.ToArray(),
            TrackPathById: pathByTrackId
        );
    }

    public bool TryGetTrackFile(string trackId, out string fullPath, out string contentType)
    {
        fullPath = "";
        contentType = "application/octet-stream";

        var snap = _snap;
        if (!snap.TrackPathById.TryGetValue(trackId, out var p)) return false;
        if (!File.Exists(p)) return false;

        fullPath = p;
        if (_types.TryGetContentType(p, out var ct)) contentType = ct;
        return true;
    }

    private static string HashHex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public record Snapshot(ArtistDto[] Artists, AlbumDto[] Albums, TrackDto[] Tracks, Dictionary<string,string> TrackPathById);
    public record ArtistDto(string Id, string Name);
    public record AlbumDto(string Id, string Name, string ArtistId);
    public record TrackDto(string Id, string Title, string Ext, string ArtistId, string ArtistName, string AlbumId, string AlbumName);
}
