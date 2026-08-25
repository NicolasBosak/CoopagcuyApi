using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
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

    // Productoras propias del margen de la reventa, para no compartir Lote
    // ni Pago con las pruebas de ganancias de arriba.
    private const string CedulaMargenIngreso = "0104576285";
    private const string CedulaMargenSinPrecio = "0104576293";
    private const string CedulaMargenCosto = "0104576301";
    private const string CedulaMargenDenominador = "0104576319";
    private const string CedulaMargenDevolucion = "0104576327";
    private const string CedulaMargenEtiqueta = "0104576335";

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
    public async Task LasProductorasSeOrdenanPorCobradoLocalPrimero_NoPorLaSuma()
    {
        // N1: orden lexicográfico (CobradoLocal, luego PagadoPlanta, luego
        // PactadoCuotas), NUNCA por la suma de las tres columnas —esa suma
        // no existe en ningún DTO ni celda del reporte, porque las tres
        // NUNCA se suman entre sí. Con estos montos, ordenar por la suma
        // pondría a la productora B primero (500 > 400); el orden correcto
        // pone a A primero porque tiene MÁS cobrado en mano, aunque su
        // suma total sea menor.
        var productoraA = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);
        var productoraB = await Sembrador.ProductoraAsync(
            api, CedulaNie, CentroAcopio.PAT, comunidadId: 2);

        await using (var db = api.NuevoDbContext())
        {
            db.Pagos.AddRange(
                new Pago
                {
                    ProductoraId = productoraA.Id,
                    MontoUsd = 400m,
                    MontoPagadoUsd = 400m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Efectivo",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                },
                new Pago
                {
                    ProductoraId = productoraB.Id,
                    MontoUsd = 500m,
                    MontoPagadoUsd = 500m,
                    FechaPago = DateTime.UtcNow,
                    MetodoPago = "Cuotas",
                    Estado = EstadoPago.Recibido,
                    EsVentaLocal = true,
                    Responsable = "Operadora de prueba"
                });
            await db.SaveChangesAsync();
        }

        var filas = await PorProductoraAsync("PAT");

        filas.Length.ShouldBe(2);
        filas[0].GetProperty("nombreProductora").GetString()
            .ShouldBe(productoraA.NombreCompleto);
        filas[1].GetProperty("nombreProductora").GetString()
            .ShouldBe(productoraB.NombreCompleto);
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

    // ── Margen de la reventa ─────────────────────────────────────────────
    //
    // La otra mitad del reporte, y la que NUNCA se suma con las ganancias de
    // productoras de arriba: un pago a una productora es ingreso para ella y
    // costo para la cooperativa, la misma fila leída desde dos lados.
    //
    // El sembrado monta la cadena completa directo a la base —Lote →
    // CuyRegistro → LoteFaenado → RegistroFaenamiento → CuyFaenamiento →
    // Despacho → DespachoCuy—, más el Pago de la productora: es más estable
    // que montar todo el flujo de faenamiento y despacho por HTTP.

    [Fact]
    public async Task ElIngresoSaleDePrecioPorCantidad()
    {
        // El total de venta se deriva de precio x cantidad, nunca se
        // guarda aparte: un despacho de 2 animales a 8.50 da 17.
        var (_, lote) = await SembrarLoteAsync(CedulaMargenIngreso, cantidadAnimales: 2);
        await SembrarDespachoAsync(lote, [1, 2], precioUnitario: 8.50m,
            cliente: "Mercado Ingreso");

        var fila = await PorClienteAsync("Mercado Ingreso");

        fila.GetProperty("ingreso").GetDecimal().ShouldBe(17m);
        // Should-fix 1: sin devoluciones el contador se queda en 0, no
        // ausente — negativo de la aserción de arriba.
        fila.GetProperty("unidadesDevueltas").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task UnDespachoSinPrecioNoBajaElIngreso()
    {
        // Un despacho sin precio no se vendió gratis: se cuenta en
        // despachosSinPrecio, no se disuelve como cero dentro del ingreso.
        // Si se contara como cero el ingreso numérico no cambiaría (0
        // aporta lo mismo que excluirlo) — lo que distingue el
        // comportamiento correcto es despachosSinPrecio, que debe quedar en
        // 1 y no en 0.
        var (_, lote) = await SembrarLoteAsync(CedulaMargenSinPrecio, cantidadAnimales: 3);
        await SembrarDespachoAsync(lote, [1, 2], precioUnitario: 5m,
            cliente: "Mercado Sin Precio");
        await SembrarDespachoAsync(lote, [3], precioUnitario: null,
            cliente: "Mercado Sin Precio");

        var fila = await PorClienteAsync("Mercado Sin Precio");

        fila.GetProperty("ingreso").GetDecimal().ShouldBe(10m);
        fila.GetProperty("despachosSinPrecio").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task ElCostoSaleDelPagoDeLaProductora()
    {
        // Una productora cobró 120 por sus 12 cuyes del lote; se
        // despacharon 3. Costo = 120 / 12 * 3 = 30. Margen = ingreso (45,
        // 3 animales a 15) - 30 = 15.
        var (productora, lote) = await SembrarLoteAsync(
            CedulaMargenCosto, cantidadAnimales: 12);
        await SembrarDespachoAsync(lote, [1, 2, 3], precioUnitario: 15m,
            cliente: "Mercado Costo");
        await SembrarPagoPlantaAsync(productora.Id, lote.Id, montoPagado: 120m);
        // Un pago de venta local en el mismo lote/productora: si la consulta
        // lo sumara al costo (Mutación 3), MontoPagado pasaría de 120 a 145
        // y el costo de 30 a (145/12)*3 = 36.25 — un número claramente
        // distinto de 30, que es lo que hace visible el error.
        await SembrarPagoVentaLocalAsync(productora.Id, lote.Id, montoPagado: 25m);

        var fila = await PorClienteAsync("Mercado Costo");

        fila.GetProperty("costoAtribuido").GetDecimal().ShouldBe(30m);
        fila.GetProperty("margen").GetDecimal().ShouldBe(15m);
    }

    [Fact]
    public async Task ElDenominadorExcluyeLoVendidoEnLaComunidad()
    {
        // Mismos 12 animales y el mismo pago de 120 que la prueba anterior,
        // pero 2 se vendieron en la comunidad: el costo por animal sale de
        // dividir entre 10, no entre 12.
        //   dividir entre 12: (120/12)*3 = 30
        //   dividir entre 10: (120/10)*3 = 36
        // Números claramente distintos: si el denominador no excluyera la
        // venta local, la prueba lo notaría.
        var (productora, lote) = await SembrarLoteAsync(
            CedulaMargenDenominador, cantidadAnimales: 12);
        await SembrarDespachoAsync(lote, [1, 2, 3], precioUnitario: 15m,
            cliente: "Mercado Denominador");
        var pagoVentaLocalId = await SembrarPagoVentaLocalAsync(
            productora.Id, lote.Id, montoPagado: 25m);
        await MarcarVentaLocalAsync(lote.Id, [4, 5], pagoVentaLocalId);
        await SembrarPagoPlantaAsync(productora.Id, lote.Id, montoPagado: 120m);

        var fila = await PorClienteAsync("Mercado Denominador");

        fila.GetProperty("costoAtribuido").GetDecimal().ShouldBe(36m);
        fila.GetProperty("margen").GetDecimal().ShouldBe(9m);
    }

    [Fact]
    public async Task UnDespachoLegadoSinDetalleDeclaraSusAnimalesSinCosto()
    {
        // Despacho legado: apunta a Lote directo, sin ninguna fila
        // DespachoCuy (el detalle por animal no existía todavía cuando se
        // registró). Antes del fix, un despacho así sumaba su ingreso
        // completo pero no aportaba NINGÚN animal al pool de costo —ni
        // como costo, ni como AnimalesSinCosto—, así que reportaba como
        // margen puro. Con el fix, sus CantidadUnidades entran declaradas
        // como sin costo.
        //
        // Ingreso = 6.00 * 4 = 24.
        //   Antes del fix: AnimalesSinCosto = 0, CostoAtribuido = 0
        //     (el despacho no aporta NINGÚN AnimalDespachado).
        //   Con el fix:    AnimalesSinCosto = 4, CostoAtribuido = 0
        //     (los 4 entran al pool sin productora conocida).
        // El costo numérico es 0 en ambos casos —no hay pago que pueda
        // cubrir animales no identificados—, así que lo que distingue el
        // comportamiento correcto del incorrecto es AnimalesSinCosto, no
        // CostoAtribuido: por eso la prueba lo verifica explícitamente.
        await SembrarDespachoLegadoAsync(
            cantidadUnidades: 4, precioUnitario: 6m, cliente: "Mercado Legado");

        var fila = await PorClienteAsync("Mercado Legado");

        fila.GetProperty("ingreso").GetDecimal().ShouldBe(24m);
        fila.GetProperty("costoAtribuido").GetDecimal().ShouldBe(0m);
        fila.GetProperty("animalesSinCosto").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task ElIngresoDescuentaLasUnidadesDevueltas()
    {
        // S1 — decisión del product owner: el ingreso es NETO de
        // devoluciones. La vista por cliente existe "para saber cuál deja
        // más" (spec, Parte 3); contar el ingreso bruto invertiría ese
        // ranking justo en el cliente que más devuelve.
        //
        // 12 cuyes, pago de planta 120 -> costo por animal = 120/12 = 10.
        // Despacho de 5 animales a $20 = ingreso BRUTO 100. Se devuelven 2
        // unidades de ESE despacho.
        //   Ingreso NETO (correcto):     (5 - 2) * 20 = 60
        //   Ingreso bruto (si no se restara la devolución): 100
        // Números claramente distintos (60 vs 100): si el fix no restara,
        // esta prueba lo notaría.
        //
        // El costo atribuido NO se ajusta por la devolución (ver el
        // comentario de la decisión en ConstruirMargen): sigue siendo
        // (120/12)*5 = 50, porque la cooperativa ya le pagó a la productora
        // por esos 5 animales y Devolucion no dice cuál volvió.
        //   Margen correcto:   60 - 50 = 10
        //   Margen sin restar: 100 - 50 = 50 (también claramente distinto)
        var (productora, lote) = await SembrarLoteAsync(
            CedulaMargenDevolucion, cantidadAnimales: 12);
        var despachoId = await SembrarDespachoAsync(lote, [1, 2, 3, 4, 5],
            precioUnitario: 20m, cliente: "Mercado Devolucion");
        await SembrarPagoPlantaAsync(productora.Id, lote.Id, montoPagado: 120m);
        await SembrarDevolucionAsync(despachoId, cantidadUnidades: 2,
            cliente: "Mercado Devolucion");

        var fila = await PorClienteAsync("Mercado Devolucion");

        fila.GetProperty("ingreso").GetDecimal().ShouldBe(60m);
        fila.GetProperty("costoAtribuido").GetDecimal().ShouldBe(50m);
        fila.GetProperty("margen").GetDecimal().ShouldBe(10m);
        // Should-fix 1: unidadesDevueltas declara las 2 unidades que ya
        // bajaron el ingreso, en vez de dejar el descuento sin rastro.
        fila.GetProperty("unidadesDevueltas").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task LaEtiquetaDelClienteConservaLasMayusculasOriginales()
    {
        // N3: la CLAVE de agrupación se normaliza (mayúsculas) para que
        // "Mercado Ñan" y "mercado ñan" no separen filas — eso sigue
        // igual. Lo que cambia es que la ETIQUETA visible ya no es esa
        // clave: conserva la forma en que se escribió el primer despacho,
        // en vez de GRITAR el nombre del cliente en mayúsculas.
        var (_, lote) = await SembrarLoteAsync(CedulaMargenEtiqueta, cantidadAnimales: 4);
        await SembrarDespachoAsync(lote, [1, 2], precioUnitario: 5m,
            cliente: "Mercado Ñan");
        await SembrarDespachoAsync(lote, [3, 4], precioUnitario: 5m,
            cliente: "mercado ñan");

        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/margen/cliente?desde={hoy}&hasta={hoy}");
        respuesta.EnsureSuccessStatusCode();
        var filas = (await respuesta.Content.ReadFromJsonAsync<JsonElement[]>())!;

        // Las dos formas de escribirlo siguen cayendo en UNA sola fila
        // (la normalización de la clave no se tocó).
        var filasDelCliente = filas.Where(f => string.Equals(
            f.GetProperty("agrupacion").GetString(), "Mercado Ñan",
            StringComparison.OrdinalIgnoreCase)).ToList();
        filasDelCliente.Count.ShouldBe(1);

        // Pero la etiqueta NO está en mayúsculas: conserva la del primer
        // despacho ("Mercado Ñan", no "MERCADO ÑAN").
        filasDelCliente[0].GetProperty("agrupacion").GetString().ShouldBe("Mercado Ñan");
        filasDelCliente[0].GetProperty("ingreso").GetDecimal().ShouldBe(20m); // 4 x 5
    }

    // ── Excel del reporte de ganancias ─────────────────────────────────
    //
    // Un libro con las cinco vistas: por CAT, por productora, por mes,
    // margen por mes y margen por cliente. El tamaño por sí solo no puede
    // distinguir un libro que perdió una hoja, o las dos advertencias de
    // AgregarHojaMargen, de uno correcto: esas omisiones cuestan unos
    // cientos de bytes contra un umbral con miles de margen. Por eso, además
    // del tamaño, la prueba abre el binario con ClosedXML y afirma la
    // estructura que sí le importa al reporte: las cinco hojas por nombre,
    // que cada una trae datos, y que las dos hojas de margen traen sus dos
    // advertencias con la cifra correcta debajo de la tabla.

    private static readonly string[] HojasEsperadas =
    [
        "Ganancias por CAT", "Ganancias por productora", "Ganancias por mes",
        "Margen por mes", "Margen por cliente"
    ];

    private static readonly string[] HojasDeMargen = ["Margen por mes", "Margen por cliente"];

    [Fact]
    public async Task ElExcelDeGananciasSeDescargaConLasCincoHojas()
    {
        // Datos para las tres vistas de ganancias (SembrarPagosAsync) y
        // para las dos de margen (lote + despacho): sin esto el libro se
        // genera igual pero con hojas vacías, que no ejercitan ni el
        // tamaño ni las advertencias de las hojas de margen.
        await SembrarPagosAsync();
        var (_, lote) = await SembrarLoteAsync(
            CedulaMargenIngreso, cantidadAnimales: 2);
        await SembrarDespachoAsync(lote, [1, 2], precioUnitario: 8.50m,
            cliente: "Mercado Ingreso");

        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/exportar/excel/ganancias?desde={hoy}&hasta={hoy}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        // Umbral fijado tras medir los dos casos reales (no adivinado):
        // un libro vacío (una sola hoja, sin datos, como el que produce la
        // Mutación 1) pesa 6118 bytes; este libro con cinco hojas y datos
        // pesa 10145. El umbral queda en el punto medio, con margen de
        // sobra a ambos lados (~1900 bytes) frente a la variación normal
        // del formato zip/xlsx. Este número por sí solo NO distingue un
        // libro que perdió una hoja o sus advertencias (unos cientos de
        // bytes) de uno correcto: para eso están las aserciones
        // estructurales de abajo.
        bytes.Length.ShouldBeGreaterThan(8000);

        using var libro = new XLWorkbook(new MemoryStream(bytes));

        libro.Worksheets.Select(h => h.Name).ShouldBe(HojasEsperadas);

        // Con estos datos sembrados, cada una de las cinco hojas trae al
        // menos una fila (la de datos está siempre en la fila 2, justo
        // debajo del encabezado, sin importar si la hoja además lleva
        // advertencias más abajo).
        foreach (var nombre in HojasEsperadas)
            libro.Worksheet(nombre).Cell(2, 1).IsEmpty().ShouldBeFalse();

        // Las dos hojas de margen: un despacho con precio (8.50 x 2 = 17,
        // sin faltante) y sin pago de planta para esa productora, así que
        // los 2 animales quedan sin costo conocido.
        foreach (var nombreMargen in HojasDeMargen)
        {
            // Should-fix 1: "Ingreso" ya no basta solo — desde que es neto
            // de devoluciones, el encabezado debe decirlo, para no chocar
            // con el ingreso BRUTO que publica el listado de despachos bajo
            // el mismo nombre de columna.
            libro.Worksheet(nombreMargen).Cell(1, 2).GetString()
                .ShouldBe("Ingreso (neto de devoluciones)");

            var textos = libro.Worksheet(nombreMargen).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain("Despachos sin precio (no se vendieron gratis): 0");
            textos.ShouldContain(
                "Animales sin costo (su productora no ha cobrado, no costaron " +
                "cero): 2");
            // Should-fix 1: tercer contador, mismo estilo que los otros dos
            // — sin devoluciones sembradas en este período, se declara en
            // cero, no se omite.
            textos.ShouldContain("Unidades devueltas (ya restadas del ingreso): 0");

            // B1: sin ?cat=, las hojas de margen dicen explícitamente que
            // cubren toda la cooperativa, y llevan el rótulo de que el
            // margen no es un resultado contable (transporte, faenamiento
            // y empaque quedan fuera).
            textos.ShouldContain(
                "Toda la cooperativa — este reporte no se filtra por centro de acopio");
            textos.ShouldContain(
                "El margen es sobre el costo de los animales: no incluye " +
                "transporte, faenamiento ni empaque, así que no es un resultado " +
                "contable de la cooperativa.");
        }

        // B1: las tres hojas de ganancias declaran su propio alcance de CAT
        // — sin ?cat=, "todos los centros de acopio".
        foreach (var nombreGanancia in new[]
            { "Ganancias por CAT", "Ganancias por productora", "Ganancias por mes" })
        {
            var textos = libro.Worksheet(nombreGanancia).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain("Centro de acopio: Todos los centros de acopio");
        }
    }

    [Fact]
    public async Task ElExcelDeGananciasConCatDeclaraElAlcanceYLoIncluyeEnElNombreDelArchivo()
    {
        // B1: filtrado por PAT, las tres hojas de ganancias deben decirlo,
        // las dos de margen deben seguir diciendo que son de TODA la
        // cooperativa (no se filtran, a propósito), y el nombre del
        // archivo debe llevar la CAT — así una persona que solo mire el
        // nombre del archivo descargado (sin abrir el libro) ya sabe que
        // está viendo un recorte.
        await SembrarPagosAsync();

        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/exportar/excel/ganancias?desde={hoy}&hasta={hoy}&cat=PAT");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var nombreArchivo = respuesta.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        nombreArchivo.ShouldNotBeNull();
        nombreArchivo.ShouldEndWith("-PAT.xlsx");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        using var libro = new XLWorkbook(new MemoryStream(bytes));

        foreach (var nombreGanancia in new[]
            { "Ganancias por CAT", "Ganancias por productora", "Ganancias por mes" })
        {
            var textos = libro.Worksheet(nombreGanancia).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain("Centro de acopio: PAT");
        }

        foreach (var nombreMargen in HojasDeMargen)
        {
            var textos = libro.Worksheet(nombreMargen).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain(
                "Toda la cooperativa — este reporte no se filtra por centro de acopio");
        }
    }

    [Fact]
    public async Task ElExcelDeMargenDeclaraElTotalDeUnidadesDevueltas()
    {
        // Should-fix 1: un despacho parcialmente devuelto debe dejar rastro
        // del total devuelto en el libro, no solo bajar el ingreso en
        // silencio. Mismo sembrado que ElIngresoDescuentaLasUnidadesDevueltas
        // (5 unidades despachadas, 2 devueltas) pero verificado en el Excel
        // en vez de en el JSON.
        var (productora, lote) = await SembrarLoteAsync(
            CedulaMargenDevolucion, cantidadAnimales: 12);
        var despachoId = await SembrarDespachoAsync(lote, [1, 2, 3, 4, 5],
            precioUnitario: 20m, cliente: "Mercado Devolucion Excel");
        await SembrarPagoPlantaAsync(productora.Id, lote.Id, montoPagado: 120m);
        await SembrarDevolucionAsync(despachoId, cantidadUnidades: 2,
            cliente: "Mercado Devolucion Excel");

        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/exportar/excel/ganancias?desde={hoy}&hasta={hoy}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        using var libro = new XLWorkbook(new MemoryStream(bytes));

        // "Margen por cliente" agrupa un solo despacho bajo "Mercado
        // Devolucion Excel": el total del contador y la celda de la fila de
        // datos deben coincidir en 2, el mismo número que ya se verificó
        // por JSON — dos superficies, un solo dato.
        var hojaCliente = libro.Worksheet("Margen por cliente");
        var textos = hojaCliente.Column(1).CellsUsed()
            .Select(c => c.GetString()).ToList();
        textos.ShouldContain("Unidades devueltas (ya restadas del ingreso): 2");

        var filaDatos = hojaCliente.RowsUsed()
            .Single(f => f.Cell(1).GetString() == "Mercado Devolucion Excel");
        filaDatos.Cell(7).GetValue<int>().ShouldBe(2);
    }

    [Fact]
    public async Task ElExcelDeGananciasSinDatosMantieneLasCincoHojasConAdvertenciasEnCero()
    {
        // El vecino sin datos de la prueba anterior: un período vacío (sin
        // sembrar nada). Es el caso legítimo más cercano al libro completo
        // —cinco hojas con estilo y encabezado, cero filas de datos— y el
        // que de verdad compite con el umbral de tamaño de la otra prueba,
        // no el libro degenerado de una sola hoja de la Mutación 1.
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");
        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/exportar/excel/ganancias?desde={hoy}&hasta={hoy}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync();

        using var libro = new XLWorkbook(new MemoryStream(bytes));
        libro.Worksheets.Select(h => h.Name).ShouldBe(HojasEsperadas);

        // Sin datos, las hojas de margen no llevan fila 2 — pero las
        // advertencias se escriben igual, en cero, porque
        // AgregarHojaMargen las agrega incondicionalmente.
        foreach (var nombreMargen in HojasDeMargen)
        {
            var textos = libro.Worksheet(nombreMargen).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain("Despachos sin precio (no se vendieron gratis): 0");
            textos.ShouldContain(
                "Animales sin costo (su productora no ha cobrado, no costaron " +
                "cero): 0");
            textos.ShouldContain("Unidades devueltas (ya restadas del ingreso): 0");
            textos.ShouldContain(
                "Toda la cooperativa — este reporte no se filtra por centro de acopio");
            textos.ShouldContain(
                "El margen es sobre el costo de los animales: no incluye " +
                "transporte, faenamiento ni empaque, así que no es un resultado " +
                "contable de la cooperativa.");
        }

        // Igual sin datos: las hojas de ganancias declaran su alcance de
        // CAT incondicionalmente.
        foreach (var nombreGanancia in new[]
            { "Ganancias por CAT", "Ganancias por productora", "Ganancias por mes" })
        {
            var textos = libro.Worksheet(nombreGanancia).Column(1)
                .CellsUsed().Select(c => c.GetString()).ToList();

            textos.ShouldContain("Centro de acopio: Todos los centros de acopio");
        }
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

    // ── Sembradores del margen de la reventa ──────────────────────────

    /// Productora con su lote de recepción y un CuyRegistro por animal
    /// (NumeroEnLote 1..cantidadAnimales), todos suyos y ninguno vendido en
    /// la comunidad todavía.
    private async Task<(Productora Productora, Lote Lote)> SembrarLoteAsync(
        string cedula, int cantidadAnimales, CentroAcopio cat = CentroAcopio.PAT)
    {
        var productora = await Sembrador.ProductoraAsync(api, cedula, cat);

        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = ("PAT-" + Guid.NewGuid().ToString("N"))[..20],
            ProductoraId = productora.Id,
            CentroAcopio = cat,
            CantidadAnimales = cantidadAnimales,
            PesoTotalGramos = cantidadAnimales * 900m,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        var registros = Enumerable.Range(1, cantidadAnimales)
            .Select(n => new CuyRegistro
            {
                LoteId = lote.Id,
                ProductoraId = productora.Id,
                NumeroEnLote = n,
                PesoGramos = 900m,
                ColorPelaje = "Blanco",
                EstadoOreja = "Normal",
                TamanoAnimal = "Normal",
                Estado = EstadoLote.Aceptado
            }).ToList();
        db.CuyRegistros.AddRange(registros);
        await db.SaveChangesAsync();

        return (productora, lote);
    }

    /// Faena y despacha los animales indicados (por NumeroEnLote) de un
    /// lote ya sembrado: una sesión de faenamiento propia por despacho, para
    /// no chocar con el índice único de DespachoCuy.CuyFaenamientoId.
    /// Devuelve el Id del despacho creado, para poder sembrarle
    /// devoluciones encima (S1).
    private async Task<int> SembrarDespachoAsync(
        Lote lote, int[] numerosEnLote, decimal? precioUnitario, string cliente)
    {
        await using var db = api.NuevoDbContext();

        var loteFaenado = new LoteFaenado
        {
            Codigo = ("FAE-" + Guid.NewGuid().ToString("N"))[..20],
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
            UnidadesFaenadas = numerosEnLote.Length,
            PesoTotalCanalGramos = numerosEnLote.Length * 600m,
            EstadoCanal = EstadoCanal.Apto
        };
        db.Faenamientos.Add(sesion);
        await db.SaveChangesAsync();

        var cuyesFaenados = numerosEnLote
            .Select(n => new CuyFaenamiento
            {
                RegistroFaenamientoId = sesion.Id,
                NumeroEnLote = n,
                PesoCanalGramos = 600m,
                Estado = EstadoCanal.Apto
            }).ToList();
        db.CuyFaenamientos.AddRange(cuyesFaenados);
        await db.SaveChangesAsync();

        var despacho = new Despacho
        {
            LoteFaenadoId = loteFaenado.Id,
            ClienteDestino = cliente,
            FechaDespacho = DateTime.UtcNow,
            CantidadUnidades = numerosEnLote.Length,
            PrecioUnitarioUsd = precioUnitario,
            Responsable = "Responsable de prueba"
        };
        db.Despachos.Add(despacho);
        await db.SaveChangesAsync();

        db.DespachoCuys.AddRange(cuyesFaenados.Select(cf => new DespachoCuy
        {
            DespachoId = despacho.Id,
            CuyFaenamientoId = cf.Id
        }));
        await db.SaveChangesAsync();

        return despacho.Id;
    }

    /// Devolución de un cliente sobre un despacho concreto (S1): el ingreso
    /// del margen se cuenta neto de estas unidades.
    private async Task SembrarDevolucionAsync(
        int despachoId, int cantidadUnidades, string cliente)
    {
        await using var db = api.NuevoDbContext();
        db.Devoluciones.Add(new Devolucion
        {
            DespachoId = despachoId,
            ClienteDevuelve = cliente,
            FechaDevolucion = DateTime.UtcNow,
            CantidadUnidades = cantidadUnidades,
            Motivo = "Producto en mal estado",
            Responsable = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    /// Despacho legado sin ninguna fila DespachoCuy: como los que registró
    /// el sistema antes de que existiera el detalle por animal. No apunta a
    /// ningún Lote ni LoteFaenado —no hace falta para ejercitar el hueco de
    /// costo, que depende solo de que Cuyes esté vacío.
    private async Task SembrarDespachoLegadoAsync(
        int cantidadUnidades, decimal? precioUnitario, string cliente)
    {
        await using var db = api.NuevoDbContext();
        db.Despachos.Add(new Despacho
        {
            ClienteDestino = cliente,
            FechaDespacho = DateTime.UtcNow,
            CantidadUnidades = cantidadUnidades,
            PrecioUnitarioUsd = precioUnitario,
            Responsable = "Responsable de prueba"
        });
        await db.SaveChangesAsync();
    }

    private async Task SembrarPagoPlantaAsync(
        int productoraId, int loteId, decimal montoPagado)
    {
        await using var db = api.NuevoDbContext();
        db.Pagos.Add(new Pago
        {
            ProductoraId = productoraId,
            LoteId = loteId,
            MontoUsd = montoPagado,
            MontoPagadoUsd = montoPagado,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Transferencia",
            Estado = EstadoPago.Pagado,
            EsVentaLocal = false,
            Responsable = "Operadora de prueba"
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> SembrarPagoVentaLocalAsync(
        int productoraId, int loteId, decimal montoPagado)
    {
        await using var db = api.NuevoDbContext();
        var pago = new Pago
        {
            ProductoraId = productoraId,
            LoteId = loteId,
            MontoUsd = montoPagado,
            MontoPagadoUsd = montoPagado,
            FechaPago = DateTime.UtcNow,
            MetodoPago = "Efectivo",
            Estado = EstadoPago.Recibido,
            EsVentaLocal = true,
            Responsable = "Operadora de prueba"
        };
        db.Pagos.Add(pago);
        await db.SaveChangesAsync();
        return pago.Id;
    }

    private async Task MarcarVentaLocalAsync(
        int loteId, int[] numerosEnLote, int pagoVentaLocalId)
    {
        await using var db = api.NuevoDbContext();
        var registros = await db.CuyRegistros
            .Where(c => c.LoteId == loteId && numerosEnLote.Contains(c.NumeroEnLote))
            .ToListAsync();
        foreach (var registro in registros)
            registro.VentaLocalPagoId = pagoVentaLocalId;
        await db.SaveChangesAsync();
    }

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

    private async Task<JsonElement> PorClienteAsync(string cliente)
    {
        var hoy = (DateTime.UtcNow + FechaUtc.DesfasePiloto).ToString("yyyy-MM-dd");

        var respuesta = await api.ComoAdmin()
            .GetAsync($"/api/reportes/margen/cliente?desde={hoy}&hasta={hoy}");
        respuesta.EnsureSuccessStatusCode();

        var filas = (await respuesta.Content.ReadFromJsonAsync<JsonElement[]>())!;
        // N3: "agrupacion" ya NO viene en mayúsculas (esa es la clave
        // interna de agrupación, no la etiqueta visible) — se compara sin
        // distinguir mayúsculas en vez de contra el .ToUpperInvariant() de
        // antes.
        return filas.Single(f => string.Equals(
            f.GetProperty("agrupacion").GetString(), cliente.Trim(),
            StringComparison.OrdinalIgnoreCase));
    }
}
