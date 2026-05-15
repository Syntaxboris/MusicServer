using Microsoft.AspNetCore.Mvc;
using MusicServer.Services;

namespace MusicServer.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly MusicLibrary _lib;
    public AdminController(MusicLibrary lib) => _lib = lib;

    [HttpPost("rescan")]
    public IActionResult Rescan()
    {
        _lib.Rescan();
        return Ok(new { ok = true, tracks = _lib.Current.Tracks.Length });
    }
}
