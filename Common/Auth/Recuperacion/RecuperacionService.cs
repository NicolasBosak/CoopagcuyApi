using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoopagcuyApi.Common.Auth.Recuperacion;

public interface IRecuperacionService
{
    Task SolicitarAsync(string cedula, string? ip);
    Task<List<SolicitudPasswordDto>> ListarAsync(bool incluirResueltas);
    Task<PasswordTemporalDto> ResolverAsync(int id, string cedulaAdmin);
    Task<bool> DescartarAsync(int id, string cedulaAdmin);
    Task<PasswordTemporalDto> RestablecerPorAdminAsync(int usuarioId, string cedulaAdmin);
    Task<bool> CambiarPasswordAsync(string cedula, string passwordActual, string passwordNueva);
}

public class RecuperacionService(
    AppDbContext db,
    ISesionService sesionService) : IRecuperacionService
{
    public async Task SolicitarAsync(string cedula, string? ip)
    {
        var cedulaNormalizada = cedula.Trim();

        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Cedula == cedulaNormalizada && u.Activo);

        // Cédula que no corresponde a ningún usuario activo: no se registra
        // nada. El endpoint es público, y persistir aquí permitiría inflar la
        // tabla desde internet. El controlador responde igual en ambos casos.
        if (usuario is null) return;

        db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
        {
            UsuarioId = usuario.Id,
            CedulaSolicitada = cedulaNormalizada,
            IpSolicitud = Recortar(ip, 60)
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Ya tenía una pendiente (índice único parcial). Se descarta el
            // cambio y se responde igual: desde fuera, "ya tenías una
            // solicitud" y "acabo de crearla" deben ser indistinguibles.
            db.ChangeTracker.Clear();
        }
    }

    public async Task<List<SolicitudPasswordDto>> ListarAsync(bool incluirResueltas)
    {
        var query = db.SolicitudesRestablecerPassword.AsNoTracking();

        if (!incluirResueltas)
            query = query.Where(s => s.Estado == EstadoSolicitudPassword.Pendiente);

        return await query
            .OrderByDescending(s => s.FechaCreacion)
            // Mismo tope que el resto de listados del sistema: el historial
            // completo no cabe en una pantalla ni hace falta en la bandeja
            .Take(300)
            .Select(s => new SolicitudPasswordDto(
                s.Id,
                s.UsuarioId,
                s.Usuario.NombreCompleto,
                s.Usuario.Cedula,
                s.Usuario.Rol.ToString(),
                s.Usuario.CatAsignado == null ? null : s.Usuario.CatAsignado.ToString(),
                s.Usuario.Activo,
                s.Estado.ToString(),
                s.Origen.ToString(),
                s.FechaCreacion,
                s.FechaResolucion,
                s.ResueltaPor))
            .ToListAsync();
    }

    public async Task<PasswordTemporalDto> ResolverAsync(int id, string cedulaAdmin)
    {
        // La conexión a Neon reintenta (EnableRetryOnFailure), y una
        // transacción explícita solo es compatible con eso dentro de una
        // execution strategy. El ChangeTracker se limpia al entrar porque en
        // un reintento las entidades cargadas antes quedarían obsoletas.
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync();

            var solicitud = await db.SolicitudesRestablecerPassword
                .Include(s => s.Usuario)
                .FirstOrDefaultAsync(s => s.Id == id)
                ?? throw new KeyNotFoundException("La solicitud no existe.");

            if (solicitud.Estado != EstadoSolicitudPassword.Pendiente)
                throw new InvalidOperationException(
                    "Otro administrador ya atendió esta solicitud.");

            // El usuario pudo desactivarse entre la solicitud y su resolución:
            // restablecerle la contraseña sería devolverle el acceso a alguien
            // que la cooperativa acaba de apartar.
            if (!solicitud.Usuario.Activo)
                throw new InvalidOperationException(
                    "El usuario está desactivado. " +
                    "Reactívalo antes de restablecer su contraseña.");

            var temporal = CredencialTemporal.Asignar(solicitud.Usuario);

            solicitud.Estado = EstadoSolicitudPassword.Resuelta;
            solicitud.FechaResolucion = DateTime.UtcNow;
            solicitud.ResueltaPor = cedulaAdmin;

            await db.SaveChangesAsync();

            // Imprescindible: si la solicitud vino porque alguien tomó la
            // tablet del operador, dejarle la sesión de 7 días viva anularía
            // el restablecimiento.
            await sesionService.RevocarUsuarioAsync(solicitud.UsuarioId);

            await transaccion.CommitAsync();

            return new PasswordTemporalDto(
                temporal,
                solicitud.Usuario.NombreCompleto,
                solicitud.Usuario.Cedula);
        });
    }

    /// <summary>
    /// Restablece la contraseña de un usuario por iniciativa del administrador,
    /// sin que medie una solicitud. Es la vía para desbloquear a quien llama por
    /// teléfono o no logra usar la pantalla de recuperación por su cuenta.
    /// </summary>
    public async Task<PasswordTemporalDto> RestablecerPorAdminAsync(
        int usuarioId, string cedulaAdmin)
    {
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaccion = await db.Database.BeginTransactionAsync();

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId)
                ?? throw new KeyNotFoundException("El usuario no existe.");

            // Restablecerse a uno mismo revocaría la propia sesión en mitad de
            // la operación: el administrador quedaría desconectado con la
            // temporal a medio leer en un modal que su sesión acaba de
            // invalidar. Para cambiar la propia está /cambiar-password.
            if (usuario.Cedula == cedulaAdmin)
                throw new InvalidOperationException(
                    "No puedes restablecer tu propia contraseña desde aquí. " +
                    "Usa la pantalla de cambiar contraseña.");

            if (!usuario.Activo)
                throw new InvalidOperationException(
                    "El usuario está desactivado. " +
                    "Reactívalo antes de restablecer su contraseña.");

            var temporal = CredencialTemporal.Asignar(usuario);
            var ahora = DateTime.UtcNow;

            // Si ya tenía una solicitud pendiente se resuelve ESA, conservando
            // su origen: esa persona sí pidió el cambio, y el administrador la
            // atendió sin pasar por el botón de la bandeja. Crear una segunda
            // fila dejaría la pendiente colgada para siempre y el administrador
            // vería una solicitud fantasma de alguien a quien ya atendió.
            var pendiente = await db.SolicitudesRestablecerPassword
                .FirstOrDefaultAsync(s => s.UsuarioId == usuarioId
                    && s.Estado == EstadoSolicitudPassword.Pendiente);

            if (pendiente is not null)
            {
                pendiente.Estado = EstadoSolicitudPassword.Resuelta;
                pendiente.FechaResolucion = ahora;
                pendiente.ResueltaPor = cedulaAdmin;
            }
            else
            {
                db.SolicitudesRestablecerPassword.Add(new SolicitudRestablecerPassword
                {
                    UsuarioId = usuario.Id,
                    CedulaSolicitada = usuario.Cedula,
                    Origen = OrigenSolicitudPassword.Administrador,
                    Estado = EstadoSolicitudPassword.Resuelta,
                    FechaCreacion = ahora,
                    FechaResolucion = ahora,
                    ResueltaPor = cedulaAdmin
                });
            }

            await db.SaveChangesAsync();

            // Igual que al resolver una solicitud: si la cuenta estaba
            // comprometida, dejarle la sesión de 7 días viva anularía el
            // restablecimiento.
            await sesionService.RevocarUsuarioAsync(usuario.Id);

            await transaccion.CommitAsync();

            return new PasswordTemporalDto(
                temporal, usuario.NombreCompleto, usuario.Cedula);
        });
    }

    public async Task<bool> DescartarAsync(int id, string cedulaAdmin)
    {
        var solicitud = await db.SolicitudesRestablecerPassword.FindAsync(id);
        if (solicitud is null) return false;

        if (solicitud.Estado != EstadoSolicitudPassword.Pendiente)
            throw new InvalidOperationException(
                "Otro administrador ya atendió esta solicitud.");

        solicitud.Estado = EstadoSolicitudPassword.Descartada;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.ResueltaPor = cedulaAdmin;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Cambia la contraseña del usuario autenticado. Devuelve false si la
    /// contraseña actual no coincide; lanza InvalidOperationException si la
    /// nueva incumple la política.
    /// </summary>
    public async Task<bool> CambiarPasswordAsync(
        string cedula, string passwordActual, string passwordNueva)
    {
        // Se valida ANTES de comprobar la actual: no tiene sentido pedirle al
        // usuario que reintente la actual si la nueva no iba a servir igual
        PoliticaPassword.Validar(passwordNueva);

        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Cedula == cedula && u.Activo)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
            return false;

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva);
        usuario.DebeCambiarPassword = false;
        await db.SaveChangesAsync();
        return true;
    }

    private static string? Recortar(string? valor, int max) =>
        string.IsNullOrWhiteSpace(valor) ? null
            : valor.Length <= max ? valor : valor[..max];
}
