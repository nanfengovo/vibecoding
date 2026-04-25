using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using QuantTrading.Api.Models;

namespace QuantTrading.Api.Services.Auth;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(AppUser user)
    {
        var secret = ResolveJwtSecret(_configuration);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresHours = int.TryParse(_configuration["Auth:JwtExpiresHours"], out var parsed) ? parsed : 168;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("displayName", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Auth:JwtIssuer"] ?? "QuantTrading",
            audience: _configuration["Auth:JwtAudience"] ?? "QuantTradingWeb",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiresHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string ResolveJwtSecret(IConfiguration configuration)
    {
        var secret = configuration["Auth:JwtSecret"];
        if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 32)
        {
            return secret;
        }

        return "QuantTrading-Development-Jwt-Secret-Change-Me-2026";
    }
}
