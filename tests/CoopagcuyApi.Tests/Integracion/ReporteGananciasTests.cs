using System.Net.Http.Json;
using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El reporte entero publica dos cifras que NUNCA se suman: lo que ganaron
/// las productoras (lo que cubren estas pruebas) y el margen de la reventa.
/// Y dentro de esta mitad hay una segunda separación: lo cobrado en venta
/// local, lo pactado a cuotas y lo pagado por la planta van en tres
/// columnas que tampoco se suman entre sí.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ReporteGananciasTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";
    private const string CedulaNie = "0102030405";

    // Fecha explícita —no por diferencia contra UtcNow— para ejercitar la
    // frontera del mes: las 02:00 UTC del 1 de septiembre son las 21:00 del
    // 31 de agosto en el CAT (UTC-5).
    private static readonly DateTime FinDeMesUtc =
        new(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LasCuotasNoSeSumanConLoCobrado()
    {
        // Obligación que el Proyecto B le dejó a este: una CAT con muchas
        // ventas a plazo veria ganancias que todavia no tiene en caja.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("cobradoLocal").GetDecimal().ShouldBe(40m);
        fila.GetProperty("pactadoCuotas").GetDecimal().ShouldBe(30m);
    }

    [Fact]
    public async Task SeSumaLoRealmentePagado_noElMontoDelTicket()
    {
        // La diferencia son los descuentos por novedades: contarlos como
        // pagados inflaría la cifra justo donde el sistema ya sabe que no lo
        // fueron.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("pagadoPlanta").GetDecimal().ShouldBe(85m);
    }

    [Fact]
    public async Task UnTicketPendienteNoCuenta()
    {
        // Es un ticket emitido que la planta todavía no ha transferido. No es
        // dinero movido.
        await SembrarPagosAsync();

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("totalPagos").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task UnPagoDeLasVeinteHorasCaeEnSuPropioMes()
    {
        // Los días del filtro son LOCALES. Agrupar por UTC mandaría un pago
        // del último día del mes al mes siguiente: es el mismo fallo que se
        // reportó como "los despachos nuevos no aparecen en Salida".
        await SembrarPagoDeFinDeMesAsync();

        var meses = await PorMesAsync();

        // El pago se sembró a las 02:00 UTC del día 1, que son las 21:00 del
        // último día del mes anterior en el CAT.
        meses.Length.ShouldBe(1);
        meses[0].GetProperty("mes").GetInt32().ShouldBe(MesAnterior());
    }

    [Fact]
    public async Task CuotasEnMinusculaTambienEsPactado()
    {
        // PagoService.MetodosVentaLocal valida el método de pago sin
        // distinguir mayúsculas (StringComparer.OrdinalIgnoreCase) y lo
        // persiste tal cual llega: un cliente que mande "cuotas" pasa la
        // validación y queda guardado en minúsculas. Si el reporte comparara
        // en forma ordinal, este pago caería en CobradoLocal —dinero que la
        // CAT todavía no tiene en mano— y el ticket (que sí compara sin
        // distinguir mayúsculas) diría "A CUOTAS" para la misma fila.
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await using (var db = api.NuevoDbContext())
        {
            db.Pagos.Add(new Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 30m,
                MontoPagadoUsd = 30m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "cuotas",
                Estado = EstadoPago.Recibido,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            });
            await db.SaveChangesAsync();
        }

        var fila = await PorCatAsync("PAT");

        fila.GetProperty("cobradoLocal").GetDecimal().ShouldBe(0m);
        fila.GetProperty("pactadoCuotas").GetDecimal().ShouldBe(30m);
    }

    [Fact]
    public async Task FiltrarPorCatSoloTraeLaProductoraDeEsaCat()
    {
        // Sin este filtro, el front-end que pide ?cat=PAT recibiría también
        // las productoras de NIE, sin ningún error ni señal de que el
        // filtro no hizo nada.
        var enPat = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);
        var enNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        await using (var db = api.NuevoDbContext())
        {
            db.Pagos.AddRange(
                new Pago
                {
                    ProductoraId = enPat.Id,
                    MontoUsd = 40m,
                    MontoPagadoUsd = 40m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                },
                new Pago
                {
                    ProductoraId = enNie.Id,
                    MontoUsd = 25m,
                    MontoPagadoUsd = 25m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                });
            await db.SaveChangesAsync();
        }

        var filas = await PorProductoraAsync("PAT");

        filas.Length.ShouldBe(1);
        filas[0].GetProperty("nombreProductora").GetString()
            .ShouldBe(enPat.NombreCompleto);
    }

    [Fact]
    public async Task FiltrarPorCatEnGananciasPorMesSoloTraeEsaCat()
    {
        // Mismo riesgo que en la vista por productora: sin este filtro, un
        // front que pida ?cat=PAT en /ganancias/mes recibiría el mes con los
        // pagos de NIE mezclados adentro, sin ningún error ni señal.
        var enPat = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);
        var enNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        await using (var db = api.NuevoDbContext())
        {
            db.Pagos.AddRange(
                new Pago
                {
                    ProductoraId = enPat.Id,
                    MontoUsd = 40m,
                    MontoPagadoUsd = 40m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                },
                new Pago
                {
                    ProductoraId = enNie.Id,
                    MontoUsd = 25m,
                    MontoPagadoUsd = 25m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                });
            await db.SaveChangesAsync();
        }

        var meses = await PorMesAsync(cat: "PAT");

        meses.Length.ShouldBe(1);
        meses[0].GetProperty("cobradoLocal").GetDecimal().ShouldBe(40m);
    }

    [Fact]
    public async Task FiltrarPorCatEnGananciasPorCatSoloTraeEsaFila()
    {
        // Las otras dos vistas de ganancias ya acotan por ?cat=; esta
        // agrupa por el mismo campo, así que antes del fix devolvía TODAS
        // las filas (una por CAT) en vez de acotar a una — mismo parámetro,
        // comportamiento distinto entre los tres endpoints hermanos.
        var enPat = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);
        var enNie = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.NIE, comunidadId: 2);

        await using (var db = api.NuevoDbContext())
        {
            db.Pagos.AddRange(
                new Pago
                {
                    ProductoraId = enPat.Id,
                    MontoUsd = 40m,
                    MontoPagadoUsd = 40m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                },
                new Pago
                {
                    ProductoraId = enNie.Id,
                    MontoUsd = 25m,
                    MontoPagadoUsd = 25m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                });
            await db.SaveChangesAsync();
        }

        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/ganancias/cat?desde={hoy}&hasta={hoy}&cat=PAT");
        respuesta.EnsureSuccessStatusCode();
        var filas = (await respuesta.Content.ReadFromJsonAsync<JsonElement[]>())!;

        filas.Length.ShouldBe(1);
        filas[0].GetProperty("centroAcopio").GetString().ShouldBe("PAT");
    }

    // ── Sembradores ─────────────────────────────────────────────────────
    // Los Pago se escriben directo a la base: es más estable que montar todo
    // el flujo de venta local / pago de planta / verificación solo para
    // llegar a las filas que estas pruebas necesitan.

    /// Para una misma productora de PAT: venta local en efectivo (40), venta
    /// local a cuotas (30), pago de planta con descuento (100 del ticket,
    /// 85 realmente pagado) y un pago pendiente (200) que no debe contar en
    /// ninguna columna.
    private async Task SembrarPagosAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        db.Pagos.AddRange(
            new Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 40m,
                MontoPagadoUsd = 40m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Efectivo",
                Estado = EstadoPago.Recibido,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            },
            new Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 30m,
                MontoPagadoUsd = 30m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Cuotas",
                Estado = EstadoPago.Recibido,
                EsVentaLocal = true,
                Responsable = "Operadora de prueba"
            },
            new Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 100m,
                MontoPagadoUsd = 85m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Transferencia",
                Estado = EstadoPago.Pagado,
                EsVentaLocal = false,
                Responsable = "Operadora de prueba"
            },
            new Pago
            {
                ProductoraId = productora.Id,
                MontoUsd = 200m,
                FechaPago = DateTime.UtcNow,
                MetodoPago = "Transferencia",
                Estado = EstadoPago.Pendiente,
                EsVentaLocal = false,
                Responsable = "Operadora de prueba"
            });
        await db.SaveChangesAsync();
    }

    private async Task SembrarPagoDeFinDeMesAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

        await using var db = api.NuevoDbContext();
        db.Pagos.Add(new Pago
        {
            ProductoraId = productora.Id,
            MontoUsd = 50m,
            MontoPagadoUsd = 50m,
            FechaPago = FinDeMesUtc,
            MetodoPago = "Efectivo",
            Estado = EstadoPago.Recibido,
            EsVentaLocal = true,
            Responsable = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    private static int MesAnterior() => FinDeMesUtc.AddMonths(-1).Month;

    // ── Llamadas HTTP ───────────────────────────────────────────────────

    private async Task<JsonElement> PorCatAsync(string cat)
    {
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/ganancias/cat?desde={hoy}&hasta={hoy}");
        respuesta.EnsureSuccessStatusCode();

        var filas = await respuesta.Content.ReadFromJsonAsync<JsonElement[]>();
        return filas!.Single(f => f.GetProperty("centroAcopio").GetString() == cat);
    }

    private async Task<JsonElement[]> PorProductoraAsync(string? cat = null)
    {
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var querystring = $"desde={hoy}&hasta={hoy}"
            + (cat is null ? "" : $"&cat={cat}");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/ganancias/productoras?{querystring}");
        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }

    private async Task<JsonElement[]> PorMesAsync(string? cat = null)
    {
        // Rango fijo que cubre FinDeMesUtc de sobra a ambos lados de la
        // frontera de mes que esa prueba ejercita, y que también cubre
        // "hoy" para las pruebas que siembran con DateTime.UtcNow.
        var querystring = "desde=2026-08-01&hasta=2026-09-30"
            + (cat is null ? "" : $"&cat={cat}");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/ganancias/mes?{querystring}");
        respuesta.EnsureSuccessStatusCode();

        return (await respuesta.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }
}
