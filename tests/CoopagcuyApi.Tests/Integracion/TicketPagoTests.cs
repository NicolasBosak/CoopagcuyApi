using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.Services;
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
