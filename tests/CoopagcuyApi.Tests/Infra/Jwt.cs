using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Emite tokens firmados con la misma clave, emisor y audiencia que configura
/// <see cref="ApiFactory"/>. Se firman aquí en vez de llamar a /api/auth/login
/// para no depender de usuarios sembrados y para no gastar el cupo del rate
/// limiter de "auth" (10 peticiones por minuto y por IP: todas las pruebas
/// salen de la misma IP y lo agotarían).
/// </summary>
public static class Jwt
{
    // Emisor y audiencia salen de ApiFactory (no se duplican aquí): son la
    // misma constante que fija las variables de entorno Jwt__Issuer y
    // Jwt__Audience, así que solo hay una fuente de verdad.

    public static string Emitir(string rol, string? cat = null,
        string cedula = "0102030405")
    {
        // Nombres de claims confirmados en Common/Auth/JwtTokenService.cs:
        // ClaimTypes.Role para el rol, "cat" para el centro de acopio y
        // "cedula" para la cédula.
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, rol),
            new("cedula", cedula),
            new(JwtRegisteredClaimNames.Sub, "1")
        };

        if (cat is not null)
            claims.Add(new Claim("cat", cat));

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.ClaveJwt)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: ApiFactory.EmisorJwt,
            audience: ApiFactory.AudienciaJwt,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
