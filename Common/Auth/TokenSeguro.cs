using System.Security.Cryptography;

namespace CoopagcuyApi.Common.Auth;

/// <summary>
/// Genera refresh tokens opacos y los reduce a un hash para almacenarlos.
/// El token en claro solo existe en tránsito y en la cookie del cliente;
/// la base guarda únicamente el hash, así que ni un administrador ni una
/// fuga de datos permiten recuperar un token vigente.
/// </summary>
public static class TokenSeguro
{
    // 256 bits de entropía criptográfica: inadivinable por fuerza bruta
    public static string GenerarTokenPlano()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        // Base64 URL-safe para que viaje limpio en una cookie
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static string Hash(string tokenPlano)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tokenPlano));
        return Convert.ToHexString(bytes);
    }
}
