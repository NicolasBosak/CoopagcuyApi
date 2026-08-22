using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Una venta local no genera trabajo para la planta y no se deja tocar por
/// ella: el dinero ya lo recibió la CAT. Y lo que se vendió deja de contar
/// para el envío.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VentaLocalYPlantaTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task UnaVentaLocalNoApareceEnLaColaDeLaPlanta()
    {
        // Feature 2 del pedido: el operador de faenamiento no debe enterarse.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/pagos/por-pagar");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldNotContain($"\"id\":{venta.PagoId}");
    }

    [Fact]
    public async Task LaPlantaNoPuedePagarUnaVentaLocal()
    {
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/pagar", new
            {
                descuentos = Array.Empty<object>(),
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // El 409 por sí solo no distingue la guarda de EsVentaLocal del
        // chequeo de Estado != Pendiente que ya existía: una venta local
        // nace Recibido, así que ese chequeo devolvería el mismo 409 aunque
        // se quitara la guarda. Lo que la guarda aporta de más — y lo único
        // que puede ponerla roja — es que el operador de planta lea que es
        // una venta local, no una frase sobre estados internos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("venta local");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.ComprobanteUrl.ShouldBeNull();
        pago.PagadoPor.ShouldBeNull();
    }

    [Fact]
    public async Task LaCatNoPuedeVerificarUnaVentaLocal()
    {
        // No hay nada que verificar: no existe transferencia ni captura.
        var venta = await VenderAsync(2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/pagos/{venta.PagoId}/verificar", new
            {
                verificadoPor = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Mismo motivo que en LaPlantaNoPuedePagarUnaVentaLocal: el 409 solo
        // lo daría igual el chequeo de Estado != Pagado que ya existía (una
        // venta local nace Recibido). Lo que la guarda de EsVentaLocal
        // protege — y lo que hace falta afirmar para que quitarla ponga roja
        // esta prueba — es que el mensaje le diga a la CAT que es una venta
        // local y no una frase sobre estados internos.
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        cuerpo.ShouldContain("venta local");

        await using var db = api.NuevoDbContext();
        var pago = await db.Pagos.AsNoTracking()
            .FirstAsync(p => p.Id == venta.PagoId);
        pago.VerificadoPor.ShouldBeNull();
        pago.FechaVerificacion.ShouldBeNull();
    }

    // ── Sembrado compartido con la Tarea 4 ────────────────────────────

    private sealed record Venta(int PagoId, int LoteId, int ProductoraId, int[] CuyIds);

    /// Entrega de 5 cuyes en PAT y venta local de los `vendidos` primeros.
    private async Task<Venta> VenderAsync(int vendidos)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 5).Select(_ => new
        {
            pesoGramos = 1300m,
            colorPelaje = "Blanco",
            estadoOreja = "Blanda",
            tamanoAnimal = "Normal"
        }).ToArray();

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

        int loteId;
        int[] ids;
        await using (var db = api.NuevoDbContext())
        {
            ids = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToArrayAsync();

            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0])
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids.Take(vendidos).ToArray(),
                montoUsd = 15m * vendidos,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoId = await db2.Pagos
            .Where(p => p.EsVentaLocal)
            .Select(p => p.Id)
            .FirstAsync();

        return new Venta(pagoId, loteId, productora.Id, ids);
    }
}
