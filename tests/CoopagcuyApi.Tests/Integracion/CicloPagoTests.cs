using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ciclo de vida del pago: emisión por la CAT, pago por la planta y
/// verificación por la CAT. Las transiciones son de un solo sentido.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class CicloPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Fact]
    public async Task UnPagoNuevoNaceEnEstadoPendiente()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        var pago = new Pago
        {
            ProductoraId = productora.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        var guardado = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == pago.Id);

        guardado.Estado.ShouldBe(EstadoPago.Pendiente);
        guardado.MontoPagadoUsd.ShouldBeNull();
        guardado.ComprobanteUrl.ShouldBeNull();
    }

    [Fact]
    public async Task UnDescuentoNoPuedeRepetirLaMismaNovedadEnElMismoPago()
    {
        // Índice único, no solo validación de servicio: dos peticiones
        // simultáneas pasarían las dos por la validación y descontarían
        // el mismo defecto dos veces.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();

        var lote = new CoopagcuyApi.Features.Productoras.Models.Lote
        {
            CodigoLote = $"PAT-{Guid.NewGuid():N}"[..12],
            CentroAcopio = CentroAcopio.PAT,
            ProductoraId = productora.Id,
            FechaRecepcion = DateTime.UtcNow,
            CantidadAnimales = 1
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var novedad = new CoopagcuyApi.Features.Recepcion.Models.Novedad
        {
            LoteId = lote.Id,
            Tipo = TipoNovedad.SignosClinicos,
            Descripcion = "lesión visible",
            RegistradoPor = "Operadora de prueba"
        };
        db.Novedades.Add(novedad);

        var pago = new Pago
        {
            ProductoraId = productora.Id,
            LoteId = lote.Id,
            MontoUsd = 120m,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "llegó muerto",
            MontoUsd = 8m,
            RegistradoPor = "Planta de prueba"
        });
        await db.SaveChangesAsync();

        db.Descuentos.Add(new DescuentoPago
        {
            PagoId = pago.Id,
            NovedadCatId = novedad.Id,
            Descripcion = "segundo intento sobre el mismo defecto",
            MontoUsd = 5m,
            RegistradoPor = "Planta de prueba"
        });

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
