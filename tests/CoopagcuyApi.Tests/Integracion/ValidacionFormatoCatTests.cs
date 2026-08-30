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
/// correcciones, salvo uno: un alta sin CAT o con un CAT mal formado NO debe
/// persistir nada, sigue siendo dato inválido. Esta clase fija esa regla —de
/// FORMA, no de catálogo— en los tres sitios de escritura, y de paso fija dos
/// de los bordes aceptados que quedaron sin prueba en el informe de la Task 4.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class ValidacionFormatoCatTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string CedulaUsuario = "0104576277";
    private const string CedulaProductora = "0111223343";

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
        // No son tres letras: es la forma la que falla, no una lista de
        // códigos conocidos (la Task 4 no compara contra NombresCat).
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
    public async Task AltaDeAdmin_sinCatAsignado_siguePermitida()
    {
        // Contraprueba: la validación de forma solo se dispara cuando el rol
        // exige un centro. Un administrador nunca lo tiene y eso no es un
        // error (ValidarCatOperador de UsuarioService).
        var respuesta = await api.ComoAdmin().PostAsJsonAsync("/api/usuarios", new
        {
            nombreCompleto = "Admin sin centro",
            cedula = CedulaUsuario,
            rol = "AdminCooperativa"
        });

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // ── ProductoraService.CrearAsync ───────────────────────────────────

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
