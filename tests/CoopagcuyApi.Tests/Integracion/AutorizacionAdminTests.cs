using System.Net;
using ClosedXML.Excel;
using CoopagcuyApi.Tests.Infra;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Integracion;

/// <summary>
/// Los dos roles de administración no son intercambiables. El técnico atiende
/// soporte: conserva vinculaciones, reportes administrativos, administración de
/// usuarios y sesiones activas, y pierde toda la operación de la cadena. El de
/// cooperativa opera y pierde las sesiones activas, pero gana la bandeja de
/// contraseñas.
///
/// Se comprueba en el API y no solo en las rutas del front: una ruta protegida
/// sin su [Authorize] correspondiente es una falsa sensación de seguridad —con
/// el token se llama igual.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public class AutorizacionAdminTests(ApiFactory api) : IAsyncLifetime
{
    public Task InitializeAsync() => api.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminCooperativa_noPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdmin().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminCooperativa_noPuedeRevocarSesiones()
    {
        var porId = await api.ComoAdmin().DeleteAsync("/api/auth/sesiones/1");
        var porUsuario = await api.ComoAdmin()
            .DeleteAsync("/api/auth/sesiones/usuario/1");

        porId.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        porUsuario.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminTecnico_siPuedeVerLasSesionesActivas()
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync("/api/auth/sesiones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LosDosAdministradores_venLaBandejaDeContrasenas()
    {
        var cooperativa = await api.ComoAdmin().GetAsync("/api/auth/recuperacion");
        var tecnico = await api.ComoAdminTecnico().GetAsync("/api/auth/recuperacion");

        cooperativa.StatusCode.ShouldBe(HttpStatusCode.OK);
        tecnico.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnOperador_noVeLaBandejaDeContrasenas()
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync("/api/auth/recuperacion");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ── El admin técnico queda acotado a soporte ──────────────────────
    // Su trabajo es atender usuarios, no operar la cadena. Pierde recepción,
    // faenamiento, despacho, productoras, pagos y los reportes del flujo
    // físico; conserva vinculaciones, reportes administrativos, usuarios y
    // sesiones.

    [Theory]
    [InlineData("/api/recepcion/lotes")]
    [InlineData("/api/faenamiento/despachos")]
    [InlineData("/api/productoras")]
    [InlineData("/api/pagos")]
    [InlineData("/api/reportes/dashboard")]
    public async Task AdminTecnico_pierdeLaOperacion(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico().GetAsync(ruta);

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/reportes/entrada")]
    [InlineData("/api/reportes/transito")]
    [InlineData("/api/reportes/salida")]
    [InlineData("/api/reportes/exportar/excel/entrada")]
    [InlineData("/api/reportes/exportar/excel/transito")]
    [InlineData("/api/reportes/exportar/excel/salida")]
    public async Task AdminTecnico_pierdeLosReportesDelFlujoOperativo(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/reportes/productoras")]
    [InlineData("/api/reportes/cat")]
    [InlineData("/api/reportes/novedades")]
    [InlineData("/api/reportes/devoluciones")]
    public async Task AdminTecnico_conservaLosReportesAdministrativos(string ruta)
    {
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminTecnico_conservaLaBandejaDeVinculaciones()
    {
        // Los endpoints de vinculación viven dentro de RecepcionController.
        // Retirar el rol de "todo el controlador" le rompería una de las
        // cuatro pantallas que conserva: esta prueba es la red que lo evita.
        var respuesta = await api.ComoAdminTecnico()
            .GetAsync("/api/recepcion/vinculaciones");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminCooperativa_conservaLaOperacion()
    {
        // Control: el recorte es del técnico, no de los dos administradores.
        var respuesta = await api.ComoAdmin().GetAsync("/api/productoras");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminTecnico_descargaElLibroGeneralSinLasHojasDelFlujo()
    {
        // La restricción no puede escaparse por la descarga: el libro general
        // llevaba una hoja de Salida, que es justo lo que este rol perdió.
        var respuesta = await api.ComoAdminTecnico().GetAsync(
            "/api/reportes/exportar/excel/general?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contenido = await respuesta.Content.ReadAsStreamAsync();
        using var libro = new XLWorkbook(contenido);
        var hojas = libro.Worksheets.Select(h => h.Name).ToList();

        hojas.ShouldNotContain("Entrada");
        hojas.ShouldNotContain("Tránsito");
        hojas.ShouldNotContain("Salida");
        hojas.ShouldContain("Productoras");
        hojas.ShouldContain("Devoluciones clientes");
    }

    [Fact]
    public async Task AdminCooperativa_descargaElLibroGeneralCompleto()
    {
        var respuesta = await api.ComoAdmin().GetAsync(
            "/api/reportes/exportar/excel/general?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contenido = await respuesta.Content.ReadAsStreamAsync();
        using var libro = new XLWorkbook(contenido);
        var hojas = libro.Worksheets.Select(h => h.Name).ToList();

        hojas.ShouldContain("Entrada");
        hojas.ShouldContain("Tránsito");
        hojas.ShouldContain("Salida");
        hojas.ShouldContain("Productoras");
    }

    [Fact]
    public async Task OperadorFaenamiento_conservaElFlujoOperativo()
    {
        // Control: el reporte de Salida es su herramienta de trabajo diaria.
        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync("/api/reportes/salida?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── El reporte de ganancias no es del OperadorCAT ──────────────────
    // La cifra que ganó su propia CAT ya la ve en las pantallas de pago; el
    // libro de ganancias además cruza clientes y márgenes de reventa de
    // TODAS las CAT, que no es su alcance. Sin esta prueba, el [Authorize]
    // del endpoint solo lo verifica quien lea el atributo.
    //
    // S2: antes solo el Excel estaba cubierto — los otros cinco endpoints
    // del reporte de ganancias (los tres de "lo que ganaron las
    // productoras" y los dos de margen) no tenían NINGUNA prueba de rol.
    // Los seis [Authorize] de esa vez eran correctos, pero sin esta red
    // alguien que "restaure" el reporte a los dos administradores
    // originales (la petición inicial nombraba solo a esos dos) rompería la
    // pestaña del OperadorFaenamiento con los tests en verde, y alguien que
    // agregara OperadorCAT expondría clientes y márgenes de todas las CAT,
    // también con todo en verde.
    //
    // /api/reportes/unidades/mes se sumó después con el mismo trío de roles
    // exacto (AdminCooperativa, AdminTecnico, OperadorFaenamiento), así que
    // comparte esta misma lista en vez de duplicarla: son siete endpoints
    // ahora, no seis.

    [Theory]
    [InlineData("/api/reportes/exportar/excel/ganancias")]
    [InlineData("/api/reportes/ganancias/productoras")]
    [InlineData("/api/reportes/ganancias/cat")]
    [InlineData("/api/reportes/ganancias/mes")]
    [InlineData("/api/reportes/margen/mes")]
    [InlineData("/api/reportes/margen/cliente")]
    [InlineData("/api/reportes/unidades/mes")]
    public async Task OperadorCAT_pierdeLosSieteEndpointsDeGananciasYUnidades(string ruta)
    {
        var respuesta = await api.ComoOperadorCat("PAT")
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Control del rol que el spec agregó a propósito ("Es más de lo que
    // pedía la petición original"): si alguna vez alguien recorta el
    // [Authorize] de estos siete endpoints a solo los dos administradores,
    // esta prueba (no solo la de arriba) lo nota. OperadorFaenamiento recibe
    // 200 en unidades/mes igual que en los seis de ganancias, no solo se
    // comprueba que OperadorCAT reciba 403.
    [Theory]
    [InlineData("/api/reportes/exportar/excel/ganancias")]
    [InlineData("/api/reportes/ganancias/productoras")]
    [InlineData("/api/reportes/ganancias/cat")]
    [InlineData("/api/reportes/ganancias/mes")]
    [InlineData("/api/reportes/margen/mes")]
    [InlineData("/api/reportes/margen/cliente")]
    [InlineData("/api/reportes/unidades/mes")]
    public async Task OperadorFaenamiento_accedeALosSieteEndpointsDeGananciasYUnidades(string ruta)
    {
        var respuesta = await api.ComoOperadorFaenamiento()
            .GetAsync($"{ruta}?desde=2026-08-01&hasta=2026-08-18");

        respuesta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
