using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicServer.Services;

namespace MusicServer.Controllers;

[ApiController]
[Route("api/music")]
[Authorize]
public class MusicController : ControllerBase
{
    private readonly MusicLibrary _lib;
    public MusicController(MusicLibrary lib) => _lib = lib;

    [HttpGet("artists")]
    public IActionResult Artists() => Ok(_lib.Current.Artists);

    [HttpGet("artists/{artistId}/albums")]
    public IActionResult Albums(string artistId)
        => Ok(_lib.Current.Albums.Where(a => a.ArtistId == artistId));

    [HttpGet("albums/{albumId}/tracks")]
    public IActionResult Tracks(string albumId, int skip = 0, int take = 200)
    {
        take = Math.Clamp(take, 1, 500);
        var q = _lib.Current.Tracks
            .Where(t => t.AlbumId == albumId)
            .OrderBy(t => t.Title)
            .Skip(skip)
            .Take(take);

        return Ok(q);
    }

[HttpGet("tracks/{trackId}/stream")]
[HttpHead("tracks/{trackId}/stream")]
public IActionResult Stream(string trackId)
{
    if (!_lib.TryGetTrackFile(trackId, out var fullPath, out var contentType))
        return NotFound();

    return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
}


    [HttpPost("rescan")]
    public IActionResult Rescan()
    {
        _lib.Rescan();
        return Ok(new { ok = true, tracks = _lib.Current.Tracks.Length });
    }
}
