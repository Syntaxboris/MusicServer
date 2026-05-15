using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MusicServer.Services;

namespace MusicServer.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserDbService _db;
    private readonly IConfiguration _cfg;

    public AuthController(UserDbService db, IConfiguration cfg) => (_db, _cfg) = (db, cfg);

    public record RegisterRequest(string Username, string Password);
    public record LoginRequest(string Username, string Password);

    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Trim().Length < 3)
            return BadRequest(new { error = "El nombre de usuario debe tener al menos 3 caracteres." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "La contraseña debe tener al menos 6 caracteres." });

        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        if (!_db.TryCreateUser(req.Username.Trim(), hash, out var userId))
            return Conflict(new { error = "El nombre de usuario ya está en uso." });

        return Ok(new { token = GenerateToken(userId, req.Username.Trim()), userId });
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        var user = _db.FindUser(req.Username ?? "");
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.Value.PasswordHash))
            return Unauthorized(new { error = "Usuario o contraseña incorrectos." });

        return Ok(new { token = GenerateToken(user.Value.Id, req.Username!), userId = user.Value.Id });
    }

    private string GenerateToken(string userId, string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddDays(double.Parse(_cfg["Jwt:ExpiryDays"] ?? "30"));

        var token = new JwtSecurityToken(
            issuer: "MusicServer",
            audience: "MusicServer",
            claims: [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username)
            ],
            expires: expiry,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
