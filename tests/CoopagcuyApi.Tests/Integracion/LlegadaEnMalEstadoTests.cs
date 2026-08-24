using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// El checklist de transporte no bloquea el envío, pero deja constancia. Y si
/// salió incompleto, el operador de planta no puede confirmar la llegada sin
/// decir si los animales llegaron bien: es el único momento en que alguien
/// puede contrastar lo que se prometió con lo que llegó.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class LlegadaEnMalEstadoTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Cedula = "0104576277";

    [Fact]
    public async Task LasClavesMarcadasSeGuardan()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias", "Ventilacion" });

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);

        mov.CondicionesClaves.ShouldNotBeNull();
        mov.CondicionesClaves.ShouldContain("JaulasLimpias");
        mov.CondicionesClaves.ShouldContain("Ventilacion");
        // Y la frase compuesta de siempre sigue ahí: reimprimir una guía
        // antigua no puede perder ese dato.
        mov.CondicionesTransporte.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConChecklistIncompletoLaPreguntaEsObligatoria()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta"
                // sin llegaronEnBuenEstado
            });

        // 409 y no 400: decidir que la respuesta es obligatoria exige mirar
        // el checklist GUARDADO, no el cuerpo de la peticion. Es el criterio
        // que ya sigue todo el modulo de pagos.
        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);
        mov.FechaRecepcionPlanta.ShouldBeNull();
    }

    [Fact]
    public async Task ConChecklistCompletoLaPreguntaSigueSiendoOpcional()
    {
        // Control: la obligatoriedad es consecuencia del checklist incompleto,
        // no una molestia nueva para todo el mundo.
        var todas = CoopagcuyApi.Features.Recepcion.Models
            .CondicionTransporte.Catalogo.Keys.ToArray();
        var (_, movId) = await MovilizarAsync(todas);

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnaMovilizacionSinClavesRegistradasNoExigeRespuesta()
    {
        // CondicionesClaves NULO representa una movilización anterior a esta
        // feature: nunca se guardó qué se marcó, así que no se puede
        // reclamar que el checklist "salió incompleto". Si NoVerificadas o
        // ClavesDe llegaran a tratar el nulo como lista vacía, el checklist
        // se vería como si le faltara TODO, y estas movilizaciones viejas
        // quedarían imposibles de confirmar sin un dato que nadie capturó.
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        await using (var db = api.NuevoDbContext())
        {
            var mov = await db.Movilizaciones.FirstAsync(m => m.Id == movId);
            mov.CondicionesClaves = null;
            await db.SaveChangesAsync();
        }

        // Confirmamos que de verdad quedó nula antes de ejercer el endpoint.
        await using (var dbVerificacion = api.NuevoDbContext())
        {
            var mov = await dbVerificacion.Movilizaciones.AsNoTracking()
                .FirstAsync(m => m.Id == movId);
            mov.CondicionesClaves.ShouldBeNull();
        }

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta"
                // sin llegaronEnBuenEstado: con claves nulas no es obligatoria.
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnNoSinNingunaCondicionSeRechaza()
    {
        // Decir que llegaron mal y no decir en qué no informa de nada.
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = Array.Empty<string>()
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnaClaveDeLlegadaDesconocidaSeRechaza()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = new[] { "SeLosComioElPerro" }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnNoConSusCondicionesSeGuarda()
    {
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = false,
                condicionesLlegada = new[] { "AnimalesGolpeados", "JaulasSucias" },
                condicionLlegada = "tres con heridas en el lomo"
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);

        mov.LlegaronEnBuenEstado.ShouldBe(false);
        mov.CondicionesLlegadaClaves.ShouldNotBeNull();
        mov.CondicionesLlegadaClaves.ShouldContain("AnimalesGolpeados");
        // La observación libre se conserva aparte: el catálogo dice QUÉ pasó
        // y el texto dice qué vio.
        mov.CondicionLlegada.ShouldBe("tres con heridas en el lomo");
    }

    [Fact]
    public async Task UnSiNoArrastraCondiciones()
    {
        // Si llegaron bien, no puede quedar un cuestionario guardado.
        var (_, movId) = await MovilizarAsync(new[] { "JaulasLimpias" });

        var respuesta = await api.ComoOperadorFaenamiento()
            .PatchAsJsonAsync($"/api/recepcion/movilizaciones/{movId}/recepcion", new
            {
                recibidoPor = "Operador de planta",
                llegaronEnBuenEstado = true,
                condicionesLlegada = new[] { "AnimalesGolpeados" }
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = api.NuevoDbContext();
        var mov = await db.Movilizaciones.AsNoTracking().FirstAsync(m => m.Id == movId);
        mov.CondicionesLlegadaClaves.ShouldBeNullOrEmpty();
    }

    /// Entrega de 3 cuyes en PAT, lote cerrado y movilizado con las
    /// condiciones indicadas. Devuelve el código del lote y el Id de la
    /// movilización.
    private async Task<(string Codigo, int MovilizacionId)> MovilizarAsync(
        string[] condiciones)
    {
        var productora = await Sembrador.ProductoraAsync(
            api, Cedula, CentroAcopio.PAT);

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

        string codigo;
        await using (var db = api.NuevoDbContext())
        {
            var loteId = await db.CuyRegistros
                .Where(c => c.ProductoraId == productora.Id)
                .Select(c => c.LoteId).FirstAsync();
            var lote = await db.Lotes.FirstAsync(l => l.Id == loteId);
            lote.Cerrado = true;
            await db.SaveChangesAsync();
            codigo = lote.CodigoLote;
        }

        var mov = await api.ComoOperadorCat("PAT")
            .PostAsJsonAsync($"/api/recepcion/lotes/{codigo}/movilizacion", new
            {
                conductor = "Conductor de prueba",
                cantidadMovilizada = 3,
                condicionesTransporte = condiciones,
                sinAntibioticos7Dias = true,
                responsableDespacho = "Responsable de prueba"
            });
        mov.EnsureSuccessStatusCode();

        await using var db2 = api.NuevoDbContext();
        var movId = await db2.Movilizaciones.Select(m => m.Id).FirstAsync();
        return (codigo, movId);
    }
}
