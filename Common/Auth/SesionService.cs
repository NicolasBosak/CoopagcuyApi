using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Common.Auth;

public interface ISesionService
{
    // Emite una sesión nueva (login): access token + refresh token
    Task<AuthTokensResultado> EmitirAsync(
        Usuario usuario, string? dispositivoId, string? userAgent, string? ip);

    // Rota el refresh token presentado y devuelve un par nuevo. null si el
    // token es inválido, expiró o fue revocado.
    Task<AuthTokensResultado?> RefrescarAsync(
        string refreshTokenPlano, string? dispositivoId, string? userAgent, string? ip);

    // Cierre de sesión del propio dispositivo
    Task RevocarPorTokenAsync(string refreshTokenPlano);

    // Administración
    Task<List<SesionActivaDto>> ListarActivasAsync(string? refreshTokenActualPlano);
    Task<bool> RevocarSesionAsync(int sesionId);
    Task<int> RevocarUsuarioAsync(int usuarioId);
}

public class SesionService(
    AppDbContext db,
    IJwtTokenService tokenService) : ISesionService
{
    // Vida absoluta de la sesión: 7 días sin extenderse con el uso
    private static readonly TimeSpan DuracionRefresh = TimeSpan.FromDays(7);

    // Vida del access token JWT. Corto frente a los 7 días de la sesión:
    // si se filtra, caduca solo; el refresh (cookie httpOnly) renueva.
    private static readonly TimeSpan DuracionAccessToken = TimeSpan.FromHours(4);

    public async Task<AuthTokensResultado> EmitirAsync(
        Usuario usuario, string? dispositivoId, string? userAgent, string? ip)
    {
        var plano = TokenSeguro.GenerarTokenPlano();
        var ahora = DateTime.UtcNow;
        var dispositivo = Recortar(dispositivoId, 100);

        // Una tablet, una sesión. Sin esto, cada inicio de sesión dejaba una
        // fila más y la pantalla de sesiones mostraba cinco entradas del mismo
        // usuario, imposible de interpretar para quien tiene que revocar una.
        //
        // Se revoca, no se borra: el rastro de auditoría se conserva.
        //
        // Sin identificador de dispositivo no se revoca nada: no hay forma de
        // saber de qué tablet viene, y revocar por usuario cerraría las
        // sesiones legítimas de las demás.
        if (dispositivo is not null)
        {
            await db.RefreshTokens
                .Where(t => t.UsuarioId == usuario.Id
                         && t.DispositivoId == dispositivo
                         && !t.Revocado)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Revocado, true)
                    .SetProperty(t => t.FechaRevocacion, ahora));
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = TokenSeguro.Hash(plano),
            DispositivoId = dispositivo,
            UserAgent = Recortar(userAgent, 300),
            IpCreacion = Recortar(ip, 60),
            FechaCreacion = ahora,
            FechaUltimoUso = ahora,
            FechaExpiracion = ahora.Add(DuracionRefresh)
        });
        await db.SaveChangesAsync();

        return ConstruirResultado(usuario, plano, ahora.Add(DuracionRefresh));
    }

    public async Task<AuthTokensResultado?> RefrescarAsync(
        string refreshTokenPlano, string? dispositivoId, string? userAgent, string? ip)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return null;

        var hash = TokenSeguro.Hash(refreshTokenPlano);
        var actual = await db.RefreshTokens
            .Include(t => t.Usuario)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (actual is null) return null;

        // Reuso de un token ya rotado o revocado: síntoma de robo. Se corta
        // toda la familia de sesiones del usuario por precaución.
        if (!actual.EstaActivo)
        {
            if (actual.Revocado && actual.ReemplazadoPorHash is not null)
                await RevocarUsuarioAsync(actual.UsuarioId);
            return null;
        }

        // El usuario pudo desactivarse tras emitir la sesión
        if (!actual.Usuario.Activo)
        {
            await RevocarUsuarioAsync(actual.UsuarioId);
            return null;
        }

        // Rotación: el token viejo queda revocado y enlazado al nuevo
        var plano = TokenSeguro.GenerarTokenPlano();
        var ahora = DateTime.UtcNow;
        var nuevoHash = TokenSeguro.Hash(plano);

        actual.Revocado = true;
        actual.FechaRevocacion = ahora;
        actual.ReemplazadoPorHash = nuevoHash;

        // El nuevo token hereda la expiración ABSOLUTA del original: la
        // sesión no se prolonga indefinidamente renovando una y otra vez.
        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = actual.UsuarioId,
            TokenHash = nuevoHash,
            DispositivoId = Recortar(dispositivoId, 100) ?? actual.DispositivoId,
            UserAgent = Recortar(userAgent, 300) ?? actual.UserAgent,
            IpCreacion = Recortar(ip, 60) ?? actual.IpCreacion,
            FechaCreacion = ahora,
            FechaUltimoUso = ahora,
            FechaExpiracion = actual.FechaExpiracion
        });
        await db.SaveChangesAsync();

        return ConstruirResultado(actual.Usuario, plano, actual.FechaExpiracion);
    }

    public async Task RevocarPorTokenAsync(string refreshTokenPlano)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenPlano)) return;

        var hash = TokenSeguro.Hash(refreshTokenPlano);
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.Revocado);
        if (token is null) return;

        token.Revocado = true;
        token.FechaRevocacion = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<SesionActivaDto>> ListarActivasAsync(string? refreshTokenActualPlano)
    {
        var hashActual = string.IsNullOrWhiteSpace(refreshTokenActualPlano)
            ? null : TokenSeguro.Hash(refreshTokenActualPlano);
        var ahora = DateTime.UtcNow;

        // Se materializa antes de describir el dispositivo: Describir es
        // código C# y EF no puede traducirlo a SQL dentro del Select.
        var filas = await db.RefreshTokens
            .AsNoTracking()
            .Include(t => t.Usuario)
            .Where(t => !t.Revocado && t.FechaExpiracion > ahora)
            .OrderByDescending(t => t.FechaUltimoUso)
            .Select(t => new
            {
                t.Id,
                t.UsuarioId,
                t.Usuario.NombreCompleto,
                t.Usuario.Cedula,
                Rol = t.Usuario.Rol.ToString(),
                CatAsignado = t.Usuario.CatAsignado == null
                    ? null : t.Usuario.CatAsignado.ToString(),
                t.DispositivoId,
                t.UserAgent,
                t.IpCreacion,
                t.FechaCreacion,
                t.FechaUltimoUso,
                t.FechaExpiracion,
                EsActual = hashActual != null && t.TokenHash == hashActual
            })
            .ToListAsync();

        return filas.Select(f => new SesionActivaDto(
            f.Id, f.UsuarioId, f.NombreCompleto, f.Cedula, f.Rol,
            f.CatAsignado, f.DispositivoId, f.UserAgent, f.IpCreacion,
            f.FechaCreacion, f.FechaUltimoUso, f.FechaExpiracion,
            f.EsActual,
            DescripcionDispositivo.Describir(f.UserAgent))).ToList();
    }

    public async Task<bool> RevocarSesionAsync(int sesionId)
    {
        var token = await db.RefreshTokens.FindAsync(sesionId);
        if (token is null || token.Revocado) return false;

        token.Revocado = true;
        token.FechaRevocacion = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<int> RevocarUsuarioAsync(int usuarioId)
    {
        var ahora = DateTime.UtcNow;
        return await db.RefreshTokens
            .Where(t => t.UsuarioId == usuarioId && !t.Revocado)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Revocado, true)
                .SetProperty(t => t.FechaRevocacion, ahora));
    }

    private AuthTokensResultado ConstruirResultado(
        Usuario usuario, string refreshPlano, DateTime refreshExpira)
    {
        var accessToken = tokenService.GenerarToken(usuario);
        var respuesta = new LoginResponseDto(
            Token: accessToken,
            NombreCompleto: usuario.NombreCompleto,
            Cedula: usuario.Cedula,
            Rol: usuario.Rol.ToString(),
            CatAsignado: usuario.CatAsignado?.ToString(),
            Expira: DateTime.UtcNow.Add(DuracionAccessToken),
            SesionExpira: refreshExpira,
            DebeCambiarPassword: usuario.DebeCambiarPassword);
        return new AuthTokensResultado(respuesta, refreshPlano, refreshExpira);
    }

    private static string? Recortar(string? valor, int max) =>
        string.IsNullOrWhiteSpace(valor) ? null
            : valor.Length <= max ? valor : valor[..max];
}
