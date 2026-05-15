using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicServer.Services;

namespace MusicServer.Controllers;

[ApiController]
[Route("api/upload")]
[Authorize]
public class UploadController : ControllerBase
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".wav" };

    private static readonly string TempBase = Path.Combine(Path.GetTempPath(), "musicserver_uploads");

    private readonly MusicLibrary _lib;
    private readonly IConfiguration _cfg;

    public UploadController(MusicLibrary lib, IConfiguration cfg)
    {
        (_lib, _cfg) = (lib, cfg);
        Directory.CreateDirectory(TempBase);
    }

    // Recibe un chunk. Cuando llega el último, ensambla y extrae el ZIP.
    [HttpPost("chunk")]
    [RequestSizeLimit(60_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
    public async Task<IActionResult> UploadChunk(
        [FromForm] string uploadId,
        [FromForm] int chunkIndex,
        [FromForm] int totalChunks,
        IFormFile file)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || uploadId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            return BadRequest(new { error = "uploadId inválido." });
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Chunk vacío." });
        if (chunkIndex < 0 || totalChunks < 1 || chunkIndex >= totalChunks)
            return BadRequest(new { error = "Índice de chunk inválido." });

        var uploadDir  = Path.Combine(TempBase, uploadId);
        Directory.CreateDirectory(uploadDir);

        var chunkPath = Path.Combine(uploadDir, $"chunk_{chunkIndex:D6}");
        await using (var fs = System.IO.File.Create(chunkPath))
            await file.CopyToAsync(fs);

        // ¿Llegaron todos los chunks?
        var received = Directory.GetFiles(uploadDir, "chunk_*").Length;
        if (received < totalChunks)
            return Ok(new { ok = true, received, totalChunks, done = false });

        // Ensamblar
        var zipPath = Path.Combine(TempBase, $"{uploadId}.zip");
        try
        {
            await using (var output = System.IO.File.Create(zipPath))
            {
                foreach (var chunk in Directory.GetFiles(uploadDir, "chunk_*").OrderBy(f => f))
                {
                    await using var input = System.IO.File.OpenRead(chunk);
                    await input.CopyToAsync(output);
                }
            }

            var result = await ExtractZip(zipPath);
            return result;
        }
        finally
        {
            Directory.Delete(uploadDir, recursive: true);
            if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
        }
    }

    private async Task<IActionResult> ExtractZip(string zipPath)
    {
        var root    = _cfg["Music:RootPath"]!;
        int filesOk = 0;
        var artists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var albums  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0) continue;

                var parts = entry.FullName
                    .Replace('\\', '/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3) continue;

                var trackExt = Path.GetExtension(parts[^1]);
                if (!AllowedExt.Contains(trackExt)) continue;

                var artist   = SanitizeName(parts[0]);
                var album    = SanitizeName(parts[1]);
                var filename = SanitizeName(Path.GetFileNameWithoutExtension(parts[^1]))
                               + trackExt.ToLowerInvariant();

                if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(album) || string.IsNullOrEmpty(filename))
                    continue;

                var dir = Path.Combine(root, artist, album);
                Directory.CreateDirectory(dir);

                var dest = Path.Combine(dir, filename);
                await using var src = entry.Open();
                await using var dst = System.IO.File.Create(dest);
                await src.CopyToAsync(dst);

                filesOk++;
                artists.Add(artist);
                albums.Add($"{artist}/{album}");
            }
        }
        catch (InvalidDataException)
        {
            return BadRequest(new { error = "El ZIP ensamblado está corrupto." });
        }

        if (filesOk == 0)
            return BadRequest(new { error = "No se encontraron canciones. Estructura esperada: Artista/Album/cancion.mp3" });

        _lib.Rescan();
        return Ok(new { ok = true, done = true, files = filesOk, artists = artists.Count, albums = albums.Count });
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean   = string.Concat(name.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c));
        return clean.Trim('.', ' ');
    }
}
