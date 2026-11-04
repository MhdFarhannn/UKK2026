using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UKK.Models;

namespace UKK.Services
{
    public sealed class JwtService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;

        public JwtService(IConfiguration configuration)
        {
            _secret = configuration["Jwt:Secret"]
                ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
            _issuer = configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
            _audience = configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
            _expirationMinutes = configuration.GetValue("Jwt:ExpirationMinutes", 120);

            if (_secret.Length < 32)
            {
                throw new InvalidOperationException("Jwt:Secret must contain at least 32 characters.");
            }

            if (_expirationMinutes < 1)
            {
                throw new InvalidOperationException("Jwt:ExpirationMinutes must be greater than zero.");
            }
        }

        public AuthResponse CreateToken(UserResponse user)
        {
            var expiresAt = DateTime.UtcNow.AddMinutes(_expirationMinutes);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new AuthResponse
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAt = expiresAt,
                User = user
            };
        }
    }
}
