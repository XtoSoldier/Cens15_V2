using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CENS15_V2.Entities;
using CENS15_V2.Models;
using CENS15_V2.Models.DTOs.AuthDTOs;
using CENS15_V2.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace CENS15.V2.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        public TokenService(IConfiguration config)
        {
            _config = config;
        }

        public AuthResponse CreateToken(Auth auth)
        {
            var jwt = _config.GetSection("Jwt");

            var expiration = DateTime.UtcNow.AddMinutes(
                double.Parse(jwt["DurationInMinutes"]));

            var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, auth.User.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, auth.Email),
        new Claim(ClaimTypes.Name, auth.User.FirstName),
        new Claim(ClaimTypes.Role, auth.User.Role.Name),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["Key"]));

            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return new AuthResponse
            {
                Token = tokenString,
                Expiration = expiration,
                Name = auth.User.FirstName,
                Role = auth.User.Role.Name
            };
        }
    }
}
