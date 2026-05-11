using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SB.Framework.Core.Util;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SB.Framework.Core.Authentication.Jwt
{
    public class JwtToken
    {
        private readonly IConfiguration _configuration;
        public JwtToken(IConfiguration configuration)
        {
            _configuration = configuration;
        }


        public string CreateToken(string jwtConfigKey, List<Claim> claims)
        {
            IConfigurationSection jwtConfig = _configuration.GetSection(jwtConfigKey);

            string key = jwtConfig.GetSection("Key").Value;
            string issuer = jwtConfig.GetSection("Issuer").Value;
            string audience = jwtConfig.GetSection("Audience").Value;
            string expiresStr = jwtConfig.GetSection("Expires").Value;

            if (!int.TryParse(expiresStr, out int expires))
            {
                expires = 120;
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));



            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);


            var Sectoken = new JwtSecurityToken(
                issuer,
                audience,
                claims,
                expires: AppTime.UtcNow.AddMinutes(expires),
                signingCredentials: credentials
              );

            var token = new JwtSecurityTokenHandler().WriteToken(Sectoken);

            return token;
        }

        public ClaimsPrincipal DecryptToken(string jwtConfigKey, string token)
        {
            IConfigurationSection jwtConfig = _configuration.GetSection(jwtConfigKey);

            string key = jwtConfig.GetSection("Key").Value;
            string issuer = jwtConfig.GetSection("Issuer").Value;
            string audience = jwtConfig.GetSection("Audience").Value;

            var handler = new JwtSecurityTokenHandler();

            //var jwtToken = handler.ReadJwtToken(token);

            var param = new TokenValidationParameters()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            };

            return handler.ValidateToken(token, param, out SecurityToken validatedToken); ;
        }
    }
}

