using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// La CAT vende parte de una jaula en la comunidad en vez de enviarla a la
/// planta. Los animales vendidos quedan marcados uno a uno: es lo que después
/// permite restarlos del envío y decir en la guía cuáles se fueron.
///
/// La regla que sostiene la feature: solo se marcan cuyes DE ESA PRODUCTORA en
/// ESE LOTE que sigan disponibles. Es la misma trazabilidad que ya gobierna
/// los descuentos del pago a la planta.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class VentaLocalTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaA = "0104576277";
    private const string CedulaB = "0102030405";

    private sealed record RespuestaPago(int Id, bool EsVentaLocal, string Estado,
        decimal MontoUsd, decimal? MontoPagadoUsd, string MetodoPago);

    [Fact]
    public async Task LosCuyesVendidosQuedanAtadosAlPago()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 4);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(2).ToArray(),
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);

        var pago = await respuesta.Content.ReadFromJsonAsync<RespuestaPago>();
        pago.ShouldNotBeNull();
        pago.EsVentaLocal.ShouldBeTrue();
        // Nace cobrada: el dinero lo recibió la propia CAT, no hay nada
        // pendiente que nadie tenga que hacer dentro del sistema.
        pago.Estado.ShouldBe("Recibido");
        pago.MontoPagadoUsd.ShouldBe(30m);

        await using var db = api.NuevoDbContext();
        var marcados = await db.CuyRegistros
            .Where(c => c.VentaLocalPagoId == pago.Id)
            .Select(c => c.Id)
            .ToListAsync();

        marcados.Count.ShouldBe(2);
        marcados.ShouldBe(cuyes.Ids.Take(2).ToList(), ignoreOrder: true);

        // Y los otros dos siguen libres para la planta.
        var libres = await db.CuyRegistros
            .CountAsync(c => c.LoteId == loteId && c.VentaLocalPagoId == null);
        libres.ShouldBe(2);
    }

    [Fact]
    public async Task NoSePuedenVenderCuyesDeOtraProductora()
    {
        // El corazón de la trazabilidad, igual que en los descuentos: la
        // operadora no puede cobrar por animales que entregó otra.
        var (loteId, deA) = await EntregaAsync(CedulaA, 2);
        var deB = await AgregarALoteAsync(loteId, CedulaB, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = deA.ProductoraId,
                loteId,
                cuyRegistroIds = deB.Ids,      // ← animales de la otra
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Pagos.AnyAsync()).ShouldBeFalse();
        (await db.CuyRegistros.AnyAsync(c => c.VentaLocalPagoId != null))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task UnCuyNoSePuedeVenderDosVeces()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await VenderAsync(loteId, cuyes.ProductoraId, cuyes.Ids.Take(1).ToArray());

        var segunda = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),   // el mismo
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Pagos.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task NoSeVendeUnLoteQueYaSeMovilizo()
    {
        // Los animales ya no están en el centro: venderlos sería vender algo
        // que viaja hacia la planta.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await MovilizarAsync(loteId, 3);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnaVentaSinCuyesSeRechaza()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = Array.Empty<int>(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LasCuotasExigenElAcuerdo()
    {
        // Sin días ni valor por día, "a cuotas" no dice nada: el ticket que se
        // lleva la productora quedaría sin las condiciones que se pactaron.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Cuotas",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnMetodoDePagoDesconocidoSeRechaza()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Trueque",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnaOperadoraDeOtroCentroNoPuedeVender()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);

        var respuesta = await api.ComoOperadorCat("NIE")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = cuyes.ProductoraId,
                loteId,
                cuyRegistroIds = cuyes.Ids.Take(1).ToArray(),
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora ajena"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VariasVentasSimultaneasNoVendenElMismoCuyDosVeces()
    {
        // Comprobar que el cuy está libre y guardar después deja una ventana:
        // el último en escribir se lleva el animal y el otro pago cobra por
        // algo que no vendió. Es el mismo defecto que tuvo el ciclo de pago
        // con dos /pagar concurrentes.
        //
        // Son OCHO contendientes y no dos: con solo dos, en caliente (que es
        // como corre siempre — con la batería completa calentando antes el
        // mismo query de EF), la ventana de la carrera se cierra sola y la
        // prueba deja de detectar que falte la guarda. Con ocho, para que
        // las ocho se serialicen perfectamente por pura suerte del scheduler
        // hace falta mucho más que una traducción SQL ya cacheada; si la
        // guarda no está puesta, es prácticamente seguro que al menos dos
        // lean "libre" a la vez y las dos escriban.
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 2);
        var elMismo = cuyes.Ids.Take(1).ToArray();

        object Cuerpo(decimal monto) => new
        {
            productoraId = cuyes.ProductoraId,
            loteId,
            cuyRegistroIds = elMismo,
            montoUsd = monto,
            metodoPago = "Efectivo",
            responsable = "Operadora de prueba"
        };

        var tareas = Enumerable.Range(1, 8)
            .Select(n => api.ComoOperadorCat("PAT")
                .PostAsJsonAsync("/api/pagos/venta-local", Cuerpo(10m + n)))
            .ToArray();

        var respuestas = await Task.WhenAll(tareas);

        respuestas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);

        await using var db = api.NuevoDbContext();

        // Los siete perdedores NO dejan un pago escrito cobrando por nada:
        // el daño real no es que la columna quede marcada dos veces (no
        // puede, es una sola columna) sino un Pago huérfano que cobra por
        // cero animales.
        (await db.Pagos.CountAsync()).ShouldBe(1);

        var pagoId = await db.Pagos.Select(p => p.Id).FirstAsync();
        var marcados = await db.CuyRegistros
            .CountAsync(c => c.VentaLocalPagoId == pagoId);
        marcados.ShouldBe(1);
    }

    [Fact]
    public async Task UnAnimalRechazadoSiSePuedeVenderLocalmente()
    {
        // Decisión del diseño: un cuy de bajo peso que la planta rechaza es
        // justo uno de los que tiene sentido colocar en la comunidad.
        // Prohibirlo obligaría a la CAT a sacarlo del sistema.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaA, CentroAcopio.PAT);

        // 900 g está por debajo del mínimo: el CAT lo marca Rechazado.
        var entrega = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/recepcion/entregas", new
            {
                centroAcopio = "PAT",
                productoraId = productora.Id,
                cuyes = new[] { new
                {
                    pesoGramos = 900m,
                    colorPelaje = "Blanco",
                    estadoOreja = "Blanda",
                    tamanoAnimal = "Pequeño"
                }},
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        int loteId, cuyId;
        await using (var db = api.NuevoDbContext())
        {
            var cuy = await db.CuyRegistros
                .FirstAsync(c => c.ProductoraId == productora.Id);
            cuy.Estado.ShouldBe(EstadoLote.Rechazado);
            loteId = cuy.LoteId;
            cuyId = cuy.Id;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = new[] { cuyId },
                montoUsd = 8m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task LosDisponiblesExcluyenLoYaVendido()
    {
        var (loteId, cuyes) = await EntregaAsync(CedulaA, 3);
        await VenderAsync(loteId, cuyes.ProductoraId, cuyes.Ids.Take(1).ToArray());

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/cuyes-disponibles/{loteId}/{cuyes.ProductoraId}");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        // Dos disponibles de los tres entregados.
        System.Text.Json.JsonDocument.Parse(cuerpo)
            .RootElement.GetArrayLength().ShouldBe(2);
    }

    // ── Sembrado ──────────────────────────────────────────────────────

    private sealed record CuyesDe(int ProductoraId, int[] Ids);

    /// Entrega real de N cuyes de esa productora en PAT. Devuelve el lote y
    /// los Id de los animales.
    private async Task<(int LoteId, CuyesDe Cuyes)> EntregaAsync(
        string cedula, int cantidad)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var ids = await EntregarAsync(productora.Id, cantidad);

        await using var db = api.NuevoDbContext();
        var loteId = await db.CuyRegistros
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.LoteId)
            .FirstAsync();

        return (loteId, new CuyesDe(productora.Id, ids));
    }

    /// Añade cuyes de OTRA productora a la jaula que ya está en armado.
    private async Task<CuyesDe> AgregarALoteAsync(
        int loteId, string cedula, int cantidad)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var ids = await EntregarAsync(productora.Id, cantidad);
        return new CuyesDe(productora.Id, ids);
    }

    private async Task<int[]> EntregarAsync(int productoraId, int cantidad)
    {
        var cuyes = Enumerable.Range(0, cantidad).Select(_ => new
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
                productoraId,
                cuyes,
                enAyunas = true,
                responsableRecepcion = "Operadora de prueba"
            });
        entrega.EnsureSuccessStatusCode();

        await using var db = api.NuevoDbContext();
        return await db.CuyRegistros
            .Where(c => c.ProductoraId == productoraId)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToArrayAsync();
    }

    private async Task VenderAsync(int loteId, int productoraId, int[] cuyIds)
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId,
                loteId,
                cuyRegistroIds = cuyIds,
                montoUsd = 15m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        respuesta.EnsureSuccessStatusCode();
    }

    private async Task MovilizarAsync(int loteId, int cantidad)
    {
        string codigo;
        await using (var db = api.NuevoDbContext())
        {
            var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
            lote.Cerrado = true;
            await db.SaveChangesAsync();
            codigo = lote.CodigoLote;
        }

        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = cantidad,
                condicionesTransporte = new[] { "JaulasLimpias" },
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });
        respuesta.EnsureSuccessStatusCode();
    }
}
