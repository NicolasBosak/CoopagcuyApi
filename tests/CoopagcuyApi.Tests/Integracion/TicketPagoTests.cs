using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Services;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Ticket imprimible del pago pendiente.
///
/// Del binario del PDF no se puede afirmar casi nada: QuestPDF comprime su
/// texto. Por eso las líneas cuyo contenido depende de una regla se componen
/// en métodos estáticos y se fijan por unidad, igual que hace la guía de
/// movilización con TextoDeclaracionSanitaria.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class TicketPagoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaProductora = "0104576277";

    [Theory]
    [InlineData(EstadoPago.Pendiente, "PENDIENTE DE PAGO")]
    [InlineData(EstadoPago.Pagado, "PAGADO — POR VERIFICAR")]
    [InlineData(EstadoPago.Recibido, "PAGO VERIFICADO")]
    public void ElEstadoSeImprimeEnCastellanoYEnMayusculas(
        EstadoPago estado, string esperado)
    {
        TicketPagoService.TextoEstado(estado).ShouldBe(esperado);
    }

    [Fact]
    public void LaLeyendaAclaraQueNoEsFactura()
    {
        // La productora se lleva este papel. Si parece una factura, lo será
        // para ella — y no lo es.
        TicketPagoService.LeyendaLegal()
            .ShouldContain("no es una factura", Case.Insensitive);
    }

    [Fact]
    public async Task ElTicketSeDescargaComoPdfNoVacio()
    {
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        // Cabecera de PDF: %PDF
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task ElOperadorDeFaenamientoTambienPuedeDescargarlo()
    {
        // Es quien va a pagar: necesita ver el ticket que tiene delante la
        // productora.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperadorDeOtroCentroRecibe404()
    {
        // 404 y no 403: confirmar que el pago existe ya filtraría información
        // de otro CAT.
        var pagoId = await PagoSembradoAsync();

        var respuesta = await api.ComoOperadorCat("NIE")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnTicketConDescuentosSeSigueDescargando()
    {
        // Las unitarias de TextosTicket construyen los objetos en memoria con
        // la navegación ya poblada: pasarían igual aunque el Include faltara.
        // Lo que ocurre en ese caso no es un texto feo, es un
        // NullReferenceException al componer el PDF — un 500 al pulsar
        // "Imprimir", y justo en el ticket que sí lleva descuentos.
        //
        // Del binario del PDF no se puede afirmar el contenido, pero sí que
        // el bloque de descuentos llegó al documento: se genera el ticket
        // ANTES de pagar (sin bloque) y DESPUÉS (con bloque, dos descuentos)
        // y se compara el tamaño. Es lo que hoy nadie comprueba: quitar el
        // Include entero deja pasar 200/%PDF igual, con el bloque vacío.
        var (pagoId, novedadIds) = await PagoConDosNovedadesAsync(
            CedulaProductora, 120m);
        novedadIds.Length.ShouldBe(2);

        var antesDePagar = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");
        antesDePagar.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytesAntes = await antesDePagar.Content.ReadAsByteArrayAsync();

        // Dos descuentos sobre dos novedades distintas de la MISMA
        // productora y lote: ejercita el bucle de maquetación y el
        // OrderBy(d => d.Id), que con un solo descuento no se ejercitan.
        var pagado = await api.ComoOperadorFaenamiento()
            .PostAsJsonAsync($"/api/pagos/{pagoId}/pagar", new
            {
                descuentos = new[]
                {
                    new { novedadCatId = novedadIds[0],
                          descripcion = "oreja calcificada, canal fuera de norma",
                          montoUsd = 17m },
                    new { novedadCatId = novedadIds[1],
                          descripcion = "lesión visible en el lomo",
                          montoUsd = 8m }
                },
                comprobanteBase64 = Sembrador.ComprobanteBase64,
                pagadoPor = "Operador de planta"
            });
        pagado.EnsureSuccessStatusCode();

        var despuesDePagar = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        despuesDePagar.StatusCode.ShouldBe(HttpStatusCode.OK);
        despuesDePagar.Content.Headers.ContentType!.MediaType
            .ShouldBe("application/pdf");
        var bytesDespues = await despuesDePagar.Content.ReadAsByteArrayAsync();
        bytesDespues[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        // El umbral NO puede ser "unos pocos cientos de bytes cualquiera":
        // pagar también cambia el estado impreso ("PENDIENTE DE PAGO" a
        // "PAGADO — POR VERIFICAR", más largo) y eso solo, sin ningún
        // descuento, ya mueve el PDF cientos de bytes por su cuenta — medido
        // quitando el Include de Descuentos, la diferencia sigue siendo de
        // ~476 bytes en vez de 0. El bloque de descuentos en sí añade
        // "Subtotal", "DESCUENTOS" y, por cada uno, la línea de la novedad +
        // la descripción sin truncar + el monto: con dos descuentos, medido
        // empíricamente con este montaje exacto, el PDF crece ~2600 bytes en
        // total (25306 → 27902). 1000 queda cómodamente por encima de los
        // ~476 bytes del solo cambio de estado (así que SÍ se pone rojo si el
        // Include desaparece) y cómodamente por debajo de los ~2600 reales
        // (así que no es frágil ante un cambio menor de fuente o compresión).
        (bytesDespues.Length - bytesAntes.Length).ShouldBeGreaterThan(1000);
    }

    [Fact]
    public async Task ElTicketDeUnaVentaLocalSeDescargaYEsMasLargo()
    {
        // Las unitarias de TextosVentaLocal construyen el Pago en memoria:
        // pasarían aunque la consulta de los animales vendidos faltara. Aquí
        // se comprueba que el bloque llega al documento.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 3).Select(_ => new
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
                .OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0]).Select(c => c.LoteId).FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids,
                montoUsd = 45m,
                metodoPago = "Cuotas",
                numeroDias = 30,
                valorPorDia = 1.5m,
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        int pagoId;
        await using (var db = api.NuevoDbContext())
            pagoId = await db.Pagos.Where(p => p.EsVentaLocal)
                .Select(p => p.Id).FirstAsync();

        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoId}/ticket");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await respuesta.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000);
        bytes[0..4].ShouldBe(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task ElTicketDePlantaSobreUnLoteConVentaParcialNoCuentaLosVendidos()
    {
        // 5 entregados, 2 vendidos en la comunidad: el ticket de la planta
        // es por los 3 que SÍ viajaron, no por los 5 de la jaula. Antes de
        // este arreglo la consulta no excluía VentaLocalPagoId y el ticket
        // imprimía "Cuyes aportados: 5" aunque la operadora hubiera creado
        // el pago viendo 3 (ListarLotesPendientesAsync sí restaba).
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

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
                .OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0]).Select(c => c.LoteId).FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids.Take(2).ToArray(),
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        // El pago de la planta se registra por los 3 que quedan, tal como lo
        // ve la operadora en /api/pagos/lotes-pendientes (ya restaba antes
        // de este arreglo).
        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 36m,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoPlanta = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id && !p.EsVentaLocal)
            .FirstAsync();

        var pesos = await new TicketPagoService(db2)
            .ObtenerPesosCuyesDelTicketAsync(pagoPlanta);

        pesos.Count.ShouldBe(3,
            "el ticket de la planta debe contar solo los animales que no " +
            "se vendieron en la comunidad");
        pesos.Sum().ShouldBe(3900m);
    }

    [Fact]
    public async Task ElTicketDeVentaLocalSobreUnLoteConVentaParcialCuentaSoloLosVendidos()
    {
        // Espejo de ElTicketDePlantaSobreUnLoteConVentaParcialNoCuentaLosVendidos,
        // pero fijando la OTRA rama de ObtenerPesosCuyesDelTicketAsync (la de
        // EsVentaLocal). Sin esta prueba, nada en la batería pinnea esa
        // rama: ElTicketDeVentaLocalDifiereDelDeLaPlanta mide longitudes de
        // PDF sobre un lote donde TODOS los cuyes se vendieron, así que si
        // alguien mutara la rama de venta local a "VentaLocalPagoId == null"
        // (el filtro de la rama de planta, invertido) el ticket imprimiría
        // "Cuyes aportados: 0" mientras sigue siendo más largo que el de
        // planta por el bloque ANIMALES VENDIDOS — la mutación pasaría en
        // verde. Es el síntoma original del arreglo 2 ("dice 15 cuando se
        // pagan 12"), ahora en la rama de venta local.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

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
                .OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
            loteId = await db.CuyRegistros
                .Where(c => c.Id == ids[0]).Select(c => c.LoteId).FirstAsync();
        }

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId,
                cuyRegistroIds = ids.Take(2).ToArray(),
                montoUsd = 30m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var pagoVl = await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id && p.EsVentaLocal)
            .FirstAsync();

        var pesos = await new TicketPagoService(db2)
            .ObtenerPesosCuyesDelTicketAsync(pagoVl);

        pesos.Count.ShouldBe(2,
            "el ticket de venta local debe contar solo los animales que " +
            "ESTA venta cobró, no los 5 de la jaula ni los 3 que quedan");
        pesos.Sum().ShouldBe(2600m);
    }

    [Fact]
    public async Task ElTicketDeVentaLocalDifiereDelDeLaPlanta()
    {
        // A/B de dos lotes IDÉNTICOS en forma —misma productora, mismos 3
        // cuyes, mismo monto, mismo responsable, sembrados con la misma
        // técnica directa a la base (misma técnica que
        // LaGuiaListaLosAnimalesVendidosEnLaComunidad en
        // GuiaMovilizacionTests)—: la ÚNICA diferencia entre los dos pagos es
        // EsVentaLocal. La versión anterior de esta prueba comparaba dos
        // pagos con productoras y montos distintos (120 vs 45), así que una
        // diferencia de tamaño no probaba que la viniera del bloque de venta
        // local: podía venir igual de esas otras diferencias. Con la forma
        // controlada, lo único que puede mover el tamaño es el encabezado
        // "VENTA LOCAL", el bloque ANIMALES VENDIDOS, el estado "VENDIDO EN
        // LA COMUNIDAD…" y la línea de método — que es exactamente lo que
        // esta prueba quiere afirmar que existe.
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var codigoPlanta = await SembrarLoteDeTresAsync(productora.Id, "020");
        var codigoVentaLocal = await SembrarLoteDeTresAsync(productora.Id, "021");

        int loteIdPlanta, loteIdVl;
        int[] idsVl;
        await using (var db = api.NuevoDbContext())
        {
            loteIdPlanta = await db.Lotes
                .Where(l => l.CodigoLote == codigoPlanta)
                .Select(l => l.Id).FirstAsync();

            var loteVl = await db.Lotes
                .SingleAsync(l => l.CodigoLote == codigoVentaLocal);
            loteIdVl = loteVl.Id;
            idsVl = await db.CuyRegistros
                .Where(c => c.LoteId == loteIdVl)
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => c.Id)
                .ToArrayAsync();
        }

        var pagoPlanta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId = loteIdPlanta,
                montoUsd = 45m,
                responsable = "Operadora de prueba"
            });
        pagoPlanta.EnsureSuccessStatusCode();

        var venta = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos/venta-local", new
            {
                productoraId = productora.Id,
                loteId = loteIdVl,
                cuyRegistroIds = idsVl,
                montoUsd = 45m,
                metodoPago = "Efectivo",
                responsable = "Operadora de prueba"
            });
        venta.EnsureSuccessStatusCode();

        int pagoPlantaId, pagoVlId;
        await using (var db = api.NuevoDbContext())
        {
            pagoPlantaId = await db.Pagos
                .Where(p => p.LoteId == loteIdPlanta && !p.EsVentaLocal)
                .Select(p => p.Id).FirstAsync();
            pagoVlId = await db.Pagos
                .Where(p => p.LoteId == loteIdVl && p.EsVentaLocal)
                .Select(p => p.Id).FirstAsync();
        }

        var respPlanta = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoPlantaId}/ticket");
        respPlanta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytesPlanta = await respPlanta.Content.ReadAsByteArrayAsync();

        var respVl = await api.ComoOperadorCat("PAT")
            .GetAsync($"/api/pagos/{pagoVlId}/ticket");
        respVl.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytesVl = await respVl.Content.ReadAsByteArrayAsync();

        // Medido empíricamente con este montaje exacto (A/B, misma
        // productora, mismos 3 cuyes, mismo monto): el ticket de venta local
        // pesa varios cientos de bytes más que el de la planta. El umbral
        // queda cómodamente por debajo del valor medido, igual que en
        // LaGuiaListaLosAnimalesVendidosEnLaComunidad.
        (bytesVl.Length - bytesPlanta.Length).ShouldBeGreaterThan(200);
    }

    /// <summary>
    /// Lote sembrado directo a la base, con detalle por animal, para poder
    /// crear dos lotes de la MISMA productora sin pasar por el armado real
    /// de jaula (que acumularía ambos en una sola). Misma técnica que
    /// SembrarLoteParaProductoraAsync en GuiaMovilizacionTests.
    /// </summary>
    private async Task<string> SembrarLoteDeTresAsync(int productoraId, string sufijo)
    {
        await using var db = api.NuevoDbContext();

        var lote = new Lote
        {
            CodigoLote = $"PAT-20260819-{sufijo}",
            ProductoraId = productoraId,
            CentroAcopio = CentroAcopio.PAT,
            CantidadAnimales = 3,
            PesoTotalGramos = 3900,
            FechaRecepcion = DateTime.UtcNow,
            Estado = EstadoLote.Aceptado,
            ResponsableRecepcion = "Operadora de prueba"
        };
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        for (var numero = 1; numero <= 3; numero++)
        {
            db.CuyRegistros.Add(new CuyRegistro
            {
                LoteId = lote.Id,
                ProductoraId = productoraId,
                NumeroEnLote = numero,
                PesoGramos = 1300,
                ColorPelaje = "Blanco",
                EstadoOreja = "Blanda",
                TamanoAnimal = "Normal",
                Estado = EstadoLote.Aceptado
            });
        }
        await db.SaveChangesAsync();

        return lote.CodigoLote;
    }

    /// <summary>
    /// Igual que Sembrador.PagoConNovedadAsync pero con DOS cuyes con signos
    /// clínicos en la misma entrega, para poder citar dos novedades distintas
    /// de la misma productora y lote en un solo pago. No se generaliza en
    /// Sembrador porque varias clases de prueba dependen de su firma actual
    /// (una sola novedad) y romperla no entra en este arreglo.
    /// </summary>
    private async Task<(int PagoId, int[] NovedadIds)> PagoConDosNovedadesAsync(
        string cedula, decimal monto)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, cedula, CentroAcopio.PAT);

        var cuyes = new object[]
        {
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "oreja calcificada" },
            new { pesoGramos = 1300m, colorPelaje = "Blanco",
                  estadoOreja = "Blanda", tamanoAnimal = "Normal",
                  signosClinicos = "lesión visible en el lomo" },
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

        int loteId;
        int[] novedadIds;
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();

            novedadIds = await db.Novedades
                .Where(n => n.LoteId == loteId
                    && n.CuyRegistro != null
                    && n.CuyRegistro.ProductoraId == productora.Id
                    && n.Tipo == TipoNovedad.SignosClinicos)
                .OrderBy(n => n.Id)
                .Select(n => n.Id)
                .ToArrayAsync();
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

        return (pagoId, novedadIds);
    }

    /// Entrega real de 3 cuyes en PAT + su ticket de $120. Devuelve el Id.
    private async Task<int> PagoSembradoAsync()
    {
        var productora = await Sembrador.ProductoraAsync(
            api, CedulaProductora, CentroAcopio.PAT);

        var cuyes = Enumerable.Range(0, 3).Select(_ => new
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
        await using (var db = api.NuevoDbContext())
        {
            loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId)
                .FirstAsync();
        }

        var pago = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync("/api/pagos", new
            {
                productoraId = productora.Id,
                loteId,
                montoUsd = 120m,
                responsable = "Operadora de prueba"
            });
        pago.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        return await db2.Pagos
            .Where(p => p.ProductoraId == productora.Id)
            .Select(p => p.Id)
            .FirstAsync();
    }
}
