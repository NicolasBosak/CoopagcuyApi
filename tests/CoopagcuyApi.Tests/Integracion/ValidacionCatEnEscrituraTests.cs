using System.Net;
using System.Net.Http.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Task 4 volvió `string` el código de CAT y con eso desaparecieron las
/// barreras que antes ponía el `enum` gratis (un valor ausente ya no cae en
/// "PAT" por ser el primero de la lista; uno mal escrito ya no revienta el
/// model binding). El brief de la Task 4 aceptó esos cinco bordes como
/// correcciones, salvo uno: un alta sin CAT o con un CAT inválido NO debe
/// persistir nada, sigue siendo dato inválido.
///
/// Renombrada en la revisión de la Task 6 (antes `ValidacionFormatoCatTests`):
/// con la tabla CentrosAcopio en pie, `ValidadorCat.ValidarCatActivoAsync` ya
/// no comprueba la FORMA del código —no queda ningún regex de forma en las
/// rutas de escritura, se quitó junto con las otras tres copias— sino que lo
/// busca en el catálogo real y exige que esté activo. Un código mal formado
/// ("PATO", "PA") y uno bien formado pero inexistente o desactivado ("ZZZ",
/// un CAT dado de baja) fallan hoy por la MISMA razón: ninguno tiene una fila
/// `Activo = true` en `CentrosAcopio`. Esta clase fija esa regla única —"todo
/// CAT de escritura debe resolver a un centro activo del catálogo", sea cual
/// sea el motivo por el que no lo hace— en los tres sitios de escritura, más
/// dos de los bordes de la Task 4 que quedaron sin prueba en su momento.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ValidacionCatEnEscrituraTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaUsuario = "0104576277";
    private const string CedulaProductora = "0111223343";

    // Único código de esta clase que se crea vía API (para el caso de centro
    // desactivado); no colisiona con ninguno sembrado ni con los que usan
    // otras clases de prueba de catálogo (ApiCentrosAcopioTests).
    private const string CodigoCentroTemporal = "ZQP";

    // Bien formado (tres letras) pero que no corresponde a ningún centro
    // real: el catálogo sembrado solo tiene PAT/NIE/HUE/NAB/PEL.
    private const string CodigoInexistente = "ZZZ";

    private static async Task LimpiarCentroAsync(ApiFactory api, string codigo)
    {
        await using var db = api.NuevoDbContext();
        db.ChangeTracker.Clear();
        var centro = await db.CentrosAcopio.FindAsync(codigo);
        if (centro is not null) db.CentrosAcopio.Remove(centro);
        await db.SaveChangesAsync();
    }

    // ── UsuarioService.CrearAsync ──────────────────────────────────────

    [Fact]
    public async Task AltaDeOperadorCat_sinCatAsignado_responde409YNoPersisteNada()
    {
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Sin centro asignado",
            cedula = CedulaUsuario,
            rol = "OperadorCAT"
            // catAsignado ausente a propósito
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Usuarios.AnyAsync(u => u.Cedula == CedulaUsuario)).ShouldBeFalse();
    }

    [Fact]
    public async Task AltaDeOperadorCat_conCatDeCuatroLetras_responde409YNoPersisteNada()
    {
        // "PATO" nunca tiene una fila activa en CentrosAcopio —ni ninguna
        // fila, de hecho— así que ValidarCatActivoAsync lo rechaza igual que
        // rechazaría un código de tres letras inexistente. Ya no hay ningún
        // regex de forma en el camino: lo único que se comprueba es el
        // catálogo.
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Centro mal escrito",
            cedula = CedulaUsuario,
            rol = "OperadorCAT",
            catAsignado = "PATO"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Usuarios.AnyAsync(u => u.Cedula == CedulaUsuario)).ShouldBeFalse();
    }

    [Fact]
    public async Task AltaDeOperadorCat_conCatBienFormadoPeroInexistente_responde409()
    {
        // La comprobación real que agregó la Task 6 y que ningún caso
        // anterior ejercitaba: un código de tres letras, mayúsculas, con la
        // forma perfecta, pero que no es ninguno de los centros reales.
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Centro inexistente",
            cedula = CedulaUsuario,
            rol = "OperadorCAT",
            catAsignado = CodigoInexistente
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Usuarios.AnyAsync(u => u.Cedula == CedulaUsuario)).ShouldBeFalse();
    }

    [Fact]
    public async Task AltaDeAdmin_sinCatAsignado_siguePermitida()
    {
        // Contraprueba: la validación de catálogo solo se dispara cuando el
        // rol exige un centro. Un administrador nunca lo tiene y eso no es
        // un error (ValidarCatOperadorAsync de UsuarioService).
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Admin sin centro",
            cedula = CedulaUsuario,
            rol = "AdminCooperativa"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ── ProductoraService.CrearAsync / ActualizarAsync ──────────────────

    [Fact]
    public async Task AltaDeProductora_conCatAsignadoVacio_responde409YNoPersisteNada()
    {
        // A diferencia de Usuario, en Productora el CAT es obligatorio
        // siempre: no hay rol que lo exima. Se prueba como AdminCooperativa
        // porque un OperadorCAT tiene su CAT sellado por el token antes de
        // llegar al servicio (ver ProductorasController.Crear).
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/productoras", new
        {
            nombreCompleto = "Productora sin centro",
            cedula = CedulaProductora,
            comunidadId = 1, // Patococha, sembrada por HasData
            catAsignado = ""
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Productoras.AnyAsync(p => p.Cedula == CedulaProductora)).ShouldBeFalse();
    }

    [Fact]
    public async Task AltaDeProductora_conCatDeDosLetras_responde409YNoPersisteNada()
    {
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/productoras", new
        {
            nombreCompleto = "Productora con centro incompleto",
            cedula = CedulaProductora,
            comunidadId = 1,
            catAsignado = "PA"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Productoras.AnyAsync(p => p.Cedula == CedulaProductora)).ShouldBeFalse();
    }

    [Fact]
    public async Task AltaDeProductora_conCatBienFormadoPeroInexistente_responde409()
    {
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/productoras", new
        {
            nombreCompleto = "Productora con centro inexistente",
            cedula = CedulaProductora,
            comunidadId = 1,
            catAsignado = CodigoInexistente
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Productoras.AnyAsync(p => p.Cedula == CedulaProductora)).ShouldBeFalse();
    }

    [Fact]
    public async Task EdicionDeProductora_conCatBienFormadoPeroInexistente_responde409()
    {
        // Cubre además la regresión de la revisión: ProductorasController.
        // Actualizar no traía try/catch para InvalidOperationException y
        // este mismo caso salía como 500 en vez de 409.
        var productora = await Sembrador.ProductoraAsync(api, CedulaProductora, "PAT");

        var respuesta = await api.ComoAdmin().PutAsJsonAsync(
            $"/api/productoras/{productora.Id}", new
            {
                nombreCompleto = productora.NombreCompleto,
                cedula = productora.Cedula,
                comunidadId = productora.ComunidadId,
                catAsignado = CodigoInexistente
            });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var db = api.NuevoDbContext();
        (await db.Productoras.SingleAsync(p => p.Id == productora.Id))
            .CatAsignado.ShouldBe("PAT");
    }

    // ── RecepcionService.RegistrarEntregaAsync ──────────────────────────

    [Fact]
    public async Task RegistroDeEntrega_conCatBienFormadoPeroInexistente_responde409()
    {
        var productora = await Sembrador.ProductoraAsync(api, CedulaProductora, "PAT");

        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/recepcion/entregas", new
        {
            centroAcopio = CodigoInexistente,
            productoraId = productora.Id,
            cuyes = new object[]
            {
                new { pesoGramos = 1300m, colorPelaje = "Blanco",
                      estadoOreja = "Blanda", tamanoAnimal = "Normal" }
            },
            enAyunas = true,
            responsableRecepcion = "Operadora de prueba"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RegistroDeEntrega_enCentroDesactivado_esRechazado()
    {
        // La regla de negocio real detrás de todo este archivo: un centro
        // dado de baja no debe recibir entregas nuevas, aunque su código
        // esté perfectamente formado y haya existido siempre.
        try
        {
            var alta = await api.ComoAdmin().PostAsJsonAsync(
                "/api/catalogos/centros-acopio",
                new { codigo = CodigoCentroTemporal, nombre = "Centro temporal", cantonId = 1 });
            alta.EnsureSuccessStatusCode();

            var baja = await api.ComoAdmin().PatchAsJsonAsync(
                $"/api/catalogos/centros-acopio/{CodigoCentroTemporal}/estado",
                new { activo = false });
            baja.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var productora = await Sembrador.ProductoraAsync(api, CedulaProductora, "PAT");

            var respuesta = await api.ComoAdmin().PostAsJsonAsync(
                "/api/recepcion/entregas", new
                {
                    centroAcopio = CodigoCentroTemporal,
                    productoraId = productora.Id,
                    cuyes = new object[]
                    {
                        new { pesoGramos = 1300m, colorPelaje = "Blanco",
                              estadoOreja = "Blanda", tamanoAnimal = "Normal" }
                    },
                    enAyunas = true,
                    responsableRecepcion = "Operadora de prueba"
                });

            respuesta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
        finally
        {
            await LimpiarCentroAsync(api, CodigoCentroTemporal);
        }
    }

    // ── RecepcionController: GET lotes/abierto sin ?cat= (borde nº1 del
    // informe de la Task 4) ────────────────────────────────────────────

    [Fact]
    public async Task ObtenerLoteAbierto_sinParametroDeCat_responde400YNoLaJaulaDePatococha()
    {
        // Antes del refactor, un enum no-nullable ausente en el binding caía
        // en default(CentroAcopio) = PAT (por ser el primer valor), así que
        // un admin sin `?cat=` veía la jaula de Patococha sin haberla
        // pedido. El informe de la Task 4 conjeturó que el reemplazo
        // devolvería 204 (cat llegando null, sin filtro que case). En la
        // práctica ASP.NET Core trata un `[FromQuery] string cat` no
        // nullable como implícitamente requerido cuando Nullable Reference
        // Types está activo (ver el .csproj): sin el parámetro, el
        // binding falla ANTES de que la acción se ejecute y contesta un 400
        // (ValidationProblemDetails) automático. Es, de hecho, mejor que el
        // 204 conjeturado: un admin que olvida `?cat=` recibe un error
        // accionable en vez de un "no hay nada" ambiguo. Se siembra una
        // jaula abierta de PAT para demostrar que, en cualquier caso, NO es
        // eso lo que se devuelve.
        var productora = await Sembrador.ProductoraAsync(api, CedulaProductora, "PAT");
        await using (var db = api.NuevoDbContext())
        {
            db.Lotes.Add(new Lote
            {
                CodigoLote = "PAT-20260830-001",
                ProductoraId = productora.Id,
                CentroAcopio = "PAT",
                CantidadAnimales = 3,
                PesoTotalGramos = 3 * 1300m,
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoLote.Aceptado,
                Cerrado = false,
                ResponsableRecepcion = "Operadora de prueba"
            });
            await db.SaveChangesAsync();
        }

        // Sin ?cat= y como AdminCooperativa (no OperadorCAT, para que
        // CatDelOperador() no rellene el filtro con el CAT del token).
        var respuesta = await api.ComoAdmin().GetAsync("/api/recepcion/lotes/abierto");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── RecepcionController: POST lotes/{codigo}/cerrar con un prefijo que
    // no es un CAT (borde nº3 del informe de la Task 4) ────────────────

    [Fact]
    public async Task CerrarLote_conPrefijoQueNoEsUnCat_comoOperadorCat_responde403()
    {
        // Antes, "XXX" no parseaba como CentroAcopio, la comprobación de
        // alcance se saltaba entera y el servicio contestaba 404 (el lote no
        // existe). Ahora "XXX" es un CAT como cualquier otro para el
        // comparador de texto: no coincide con el del operador y el
        // resultado es 403, antes incluso de preguntarle al servicio si el
        // lote existe.
        var respuesta = await api.ComoOperadorCat("PAT")
            .PostAsync("/api/recepcion/lotes/XXX-1/cerrar", null);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
