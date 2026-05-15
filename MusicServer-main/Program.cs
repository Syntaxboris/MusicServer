using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(o =>
    o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddSingleton<MusicServer.Services.MusicLibrary>();
builder.Services.AddSingleton<MusicServer.Services.UserDbService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key no está configurado en appsettings.json");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer   = true,
            ValidIssuer      = "MusicServer",
            ValidateAudience = true,
            ValidAudience    = "MusicServer",
            ValidateLifetime = true,
            ClockSkew        = TimeSpan.Zero
        };
        // Permite token como query param para streaming de audio en el navegador
        opts.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var t = ctx.Request.Query["token"];
                if (!string.IsNullOrEmpty(t)) ctx.Token = t;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Admin: solo desde localhost
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/admin"))
    {
        var ip = ctx.Connection.RemoteIpAddress;
        var isLocal = ip is not null &&
            (IPAddress.IsLoopback(ip) || ip.Equals(ctx.Connection.LocalIpAddress));
        if (!isLocal)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsync("Forbidden (localhost only)");
            return;
        }
    }
    await next();
});

var root = app.Configuration["Music:RootPath"];
app.Logger.LogInformation("Music:RootPath={Root} Exists={Exists}", root, Directory.Exists(root));

var lib = app.Services.GetRequiredService<MusicServer.Services.MusicLibrary>();
lib.Rescan();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
