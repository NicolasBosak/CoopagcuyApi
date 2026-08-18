using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Alta de entidades para las pruebas. Devuelve la entidad ya guardada porque
/// Respawn trunca SIN RESTART IDENTITY: ninguna prueba puede asumir que la
/// primera fila sembrada tenga Id 1.
/// </summary>
public static class Sembrador
{
    public const string PasswordPorDefecto = "clave1234";

    public static async Task<Usuario> UsuarioAsync(
        ApiFactory api,
        string cedula,
        RolUsuario rol = RolUsuario.OperadorCAT,
        CentroAcopio? cat = CentroAcopio.PAT,
        bool activo = true,
        string password = PasswordPorDefecto)
    {
        await using var db = api.NuevoDbContext();

        var usuario = new Usuario
        {
            NombreCompleto = $"Usuario {cedula}",
            Cedula = cedula,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol,
            CatAsignado = rol == RolUsuario.OperadorCAT ? cat : null,
            Activo = activo
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    /// <summary>
    /// Alta de productoras para las pruebas de alcance. Las comunidades están
    /// sembradas con HasData y sus Id son estables: 1 Patococha (PAT),
    /// 2 Las Nieves (NIE), 3 Huertas (HUE), 4 Nabón/El Progreso (NAB),
    /// 5 Pelincay (PEL).
    ///
    /// La cédula debe ser válida según el algoritmo ecuatoriano: ProductoraService
    /// la revalida al crear, así que un número inventado reventaría la prueba por
    /// un motivo que no tiene que ver con lo que verifica.
    /// </summary>
    public static async Task<Productora> ProductoraAsync(
        ApiFactory api,
        string cedula,
        CentroAcopio cat = CentroAcopio.PAT,
        int comunidadId = 1,
        bool activa = true)
    {
        await using var db = api.NuevoDbContext();

        var productora = new Productora
        {
            NombreCompleto = $"Productora {cedula}",
            Cedula = cedula,
            ComunidadId = comunidadId,
            CatAsignado = cat,
            Activa = activa
        };

        db.Productoras.Add(productora);
        await db.SaveChangesAsync();
        return productora;
    }

    /// <summary>
    /// Inserta un despacho directamente en la base, sin pasar por
    /// RegistrarDespachoAsync. Aísla la consulta del reporte del camino de
    /// registro: si el reporte encuentra este despacho pero no los de
    /// producción, el fallo no está en la consulta.
    ///
    /// LoteFaenadoId y LoteId quedan nulos a propósito: ReporteSalidaAsync
    /// contempla ese caso y devuelve "—" como código de lote, así que no hace
    /// falta montar toda la cadena de faenamiento para ejercitar el filtro
    /// por fecha, que es lo que se está investigando.
    /// </summary>
    public static async Task<Despacho> DespachoAsync(
        ApiFactory api,
        DateTime fechaDespacho,
        string cliente = "Cliente de prueba")
    {
        await using var db = api.NuevoDbContext();

        var despacho = new Despacho
        {
            ClienteDestino = cliente,
            FechaDespacho = DateTime.SpecifyKind(fechaDespacho, DateTimeKind.Utc),
            CantidadUnidades = 3,
            Responsable = "Responsable de prueba",
            Chofer = "Chofer de prueba",
            Ruta = "Ruta de prueba",
            TipoMercado = "Local",
            Ciudad = "Cuenca",
            Pais = "Ecuador"
        };

        db.Despachos.Add(despacho);
        await db.SaveChangesAsync();
        return despacho;
    }
}
