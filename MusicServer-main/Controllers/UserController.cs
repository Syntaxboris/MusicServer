using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MusicServer.Services;

namespace MusicServer.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserDbService _db;
    private readonly MusicLibrary _lib;

    public UserController(UserDbService db, MusicLibrary lib) => (_db, _lib) = (db, lib);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ── Favorites ────────────────────────────────────────────────────────────

    [HttpGet("favorites")]
    public IActionResult GetFavorites()
    {
        var ids = _db.GetFavorites(UserId);
        var snap = _lib.Current;
        var tracks = ids.Select(id => snap.Tracks.FirstOrDefault(t => t.Id == id)).Where(t => t is not null);
        return Ok(tracks);
    }

    [HttpPost("favorites/{trackId}")]
    public IActionResult AddFavorite(string trackId)
    {
        if (!_lib.Current.Tracks.Any(t => t.Id == trackId))
            return NotFound(new { error = "Track no encontrada." });
        _db.AddFavorite(UserId, trackId);
        return Ok(new { ok = true });
    }

    [HttpDelete("favorites/{trackId}")]
    public IActionResult RemoveFavorite(string trackId)
    {
        _db.RemoveFavorite(UserId, trackId);
        return Ok(new { ok = true });
    }

    // ── Playlists ────────────────────────────────────────────────────────────

    [HttpGet("playlists")]
    public IActionResult GetPlaylists() => Ok(_db.GetPlaylists(UserId));

    public record CreatePlaylistRequest(string Name);

    [HttpPost("playlists")]
    public IActionResult CreatePlaylist([FromBody] CreatePlaylistRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "El nombre es obligatorio." });
        return Ok(_db.CreatePlaylist(UserId, req.Name.Trim()));
    }

    [HttpDelete("playlists/{playlistId}")]
    public IActionResult DeletePlaylist(string playlistId)
        => _db.DeletePlaylist(UserId, playlistId) ? Ok(new { ok = true }) : NotFound();

    [HttpGet("playlists/{playlistId}/tracks")]
    public IActionResult GetPlaylistTracks(string playlistId)
    {
        var ids = _db.GetPlaylistTracks(UserId, playlistId);
        if (ids is null) return NotFound();
        var snap = _lib.Current;
        var tracks = ids.Select(id => snap.Tracks.FirstOrDefault(t => t.Id == id)).Where(t => t is not null);
        return Ok(tracks);
    }

    public record AddTrackRequest(string TrackId);

    [HttpPost("playlists/{playlistId}/tracks")]
    public IActionResult AddTrackToPlaylist(string playlistId, [FromBody] AddTrackRequest req)
    {
        if (!_lib.Current.Tracks.Any(t => t.Id == req.TrackId))
            return NotFound(new { error = "Track no encontrada." });
        if (!_db.AddTrackToPlaylist(UserId, playlistId, req.TrackId))
            return NotFound(new { error = "Playlist no encontrada." });
        return Ok(new { ok = true });
    }

    [HttpDelete("playlists/{playlistId}/tracks/{trackId}")]
    public IActionResult RemoveTrackFromPlaylist(string playlistId, string trackId)
    {
        _db.RemoveTrackFromPlaylist(UserId, playlistId, trackId);
        return Ok(new { ok = true });
    }

    // ── Play history / más escuchadas ────────────────────────────────────────

    [HttpPost("played/{trackId}")]
    public IActionResult RecordPlay(string trackId)
    {
        if (!_lib.Current.Tracks.Any(t => t.Id == trackId))
            return NotFound(new { error = "Track no encontrada." });
        _db.RecordPlay(UserId, trackId);
        return Ok(new { ok = true });
    }

    [HttpGet("most-played")]
    public IActionResult MostPlayed([FromQuery] int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 100);
        var played = _db.GetMostPlayed(UserId, limit);
        var snap = _lib.Current;
        var result = played
            .Select(p => new { track = snap.Tracks.FirstOrDefault(t => t.Id == p.TrackId), playCount = p.Count })
            .Where(x => x.track is not null);
        return Ok(result);
    }
}
