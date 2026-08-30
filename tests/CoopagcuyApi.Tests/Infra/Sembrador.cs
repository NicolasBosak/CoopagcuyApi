using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

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
        string? cat = "PAT",
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
        string cat = "PAT",
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
    /// Cadena mínima hasta tener animales despachables: jaula → sesión de
    /// faenamiento → lote faenado. Devuelve el Id del lote faenado y los Id
    /// de los cuyes faenados, listos para POSTear a /api/faenamiento/despachos.
    ///
    /// Compartido entre DespachoExtremoAExtremoTests y PrecioDeVentaTests:
    /// ambas necesitan exactamente este mismo montaje antes de registrar un
    /// despacho por HTTP, y duplicarlo abriría la puerta a que una de las
    /// dos copias se desincronice en silencio de la otra.
    /// </summary>
    public static async Task<(int LoteFaenadoId, List<int> CuyIds)> DespachableAsync(
        ApiFactory api,
        string cedulaProductora)
    {
        var productora = await ProductoraAsync(
            api, cedulaProductora, "PAT", comunidadId: 1);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = "PAT-20260818-001",
            ProductoraId = productora.Id,
            CentroAcopio = "PAT",
            CantidadAnimales = 2,
            PesoTotalGramos = 1800,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Nicolas Nieves"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var loteFaenado = new LoteFaenado
        {
            Codigo = "FAE-20260818-001",
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba"
        };
        db.LotesFaenados.Add(loteFaenado);
        await db.SaveChangesAsync();

        var sesion = new RegistroFaenamiento
        {
            LoteId = lote.Id,
            LoteFaenadoId = loteFaenado.Id,
            NumeroSesion = 1,
            FechaFaenamiento = DateTime.UtcNow,
            OperarioResponsable = "Operario de prueba",
            UnidadesFaenadas = 2,
            PesoTotalCanalGramos = 1200,
            EstadoCanal = EstadoCanal.Apto
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        var cuyes = new[]
        {
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 1,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            },
            new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id, NumeroEnLote = 2,
                PesoCanalGramos = 600, Estado = EstadoCanal.Apto
            }
        };
        db.CuyFaenamientos.AddRange(cuyes);
        await db.SaveChangesAsync();

        return (loteFaenado.Id, cuyes.Select(c => c.Id).ToList());
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

    /// <summary>
    /// Jaula abierta en un centro, sin animales. Es lo mínimo que impide
    /// desactivar ese centro: hay cuyes físicamente esperando en él.
    /// </summary>
    public static async Task<Lote> LoteAbiertoAsync(ApiFactory api, string cat)
    {
        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = $"{cat}-20260101-001",
            CentroAcopio = cat,
            FechaRecepcion = new DateTime(2026, 1, 1),
            CantidadAnimales = 0,
            PesoTotalGramos = 0,
            Cerrado = false,
        };

        db.Lotes.Add(lote);
        await db.SaveChangesAsync();
        return lote;
    }

    /// JPEG mínimo válido —SOI + APP0 + EOI— en base64. Sirve como captura de
    /// transferencia en cualquier prueba que tenga que pagar un ticket.
    private static readonly byte[] JpegMinimo =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    public static string ComprobanteBase64 => Convert.ToBase64String(JpegMinimo);

    /// <summary>
    /// Entrega real de dos cuyes en PAT —uno con signos clínicos, para que el
    /// CAT le genere una novedad ligada a ese animal— y su ticket por el monto
    /// indicado. Devuelve el Id del pago y el de la novedad, que es lo que
    /// hace falta para citar un descuento trazable.
    /// </summary>
    public static async Task<(int PagoId, int NovedadId)> PagoConNovedadAsync(
        ApiFactory api, string cedula, decimal monto)
    {
        var productora = await ProductoraAsync(api, cedula, "PAT");

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesion-visible" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId, novedadId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadId = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id
                    && n.Tipo == TipoNovedad.SignosClinicos)
                .Select(n => n.Id)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();

        return (pagoId, novedadId);
    }

    /// <summary>
    /// Igual que <see cref="PagoConNovedadAsync"/> —dos cuyes, uno con
    /// signosClinicos— pero el cuy CON la novedad se vende en la comunidad
    /// ANTES de crear el pago de planta: nunca llegó a la planta, aunque su
    /// novedad siga en la tabla. Para el Arreglo 5 de la revisión final
    /// (ListarCuyesConNovedadAsync y ValidarDescuentosAsync no deben dejar
    /// citar la novedad de un animal que ya se vendió en la comunidad).
    /// </summary>
    public static async Task<(int PagoId, int NovedadId)> PagoConNovedadDeCuyVendidoAsync(
        ApiFactory api, string cedula, decimal monto)
    {
        var productora = await ProductoraAsync(api, cedula, "PAT");

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesion-visible" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = (string?)null },
        };

        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId, novedadId, cuyVendidoId;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadId = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id
                    && n.Tipo == TipoNovedad.SignosClinicos)
                .Select(n => n.Id)
                .FirstAsync();

            cuyVendidoId = await db.Novedades
                .Where(n => n.Id == novedadId)
                .Select(n => n.CuyRegistroId!.Value)
                .FirstAsync();
        }

        // El cuy con la novedad se vende en la comunidad: nunca llega a la
        // planta, aunque su novedad siga registrada bajo el mismo lote y
        // productora.
        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = new[] { cuyVendidoId },
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = monto,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        // Dos pagos existen ahora para esta productora: el de la venta local
        // y el de la planta. Se cita el de la planta explícitamente porque es
        // el que las pruebas del Arreglo 5 necesitan.
        var pagoId = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id && !p.EsVentaLocal)
            .Select(p => p.Id)
            .FirstAsync();

        return (pagoId, novedadId);
    }
}
