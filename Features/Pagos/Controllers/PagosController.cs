using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Features.Pagos.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Pagos.Controllers;

/// <summary>
/// Registro digital de pagos a productoras en el CAT.
/// Reemplaza el cuaderno manual identificado en el diagnóstico.
/// </summary>
[ApiController]
[Route("api/pagos")]
// Sin [Authorize] a nivel de clase: ASP.NET Core combina (con Y, no
// reemplaza) el Authorize de la clase con el de cada acción, así que un
// [Authorize(Roles=...)] aquí arriba bloquearía a OperadorFaenamiento en el
// endpoint del ticket aunque su propio atributo lo admita. Cada acción
// declara su propia lista de roles.
public class PagosController(IPagoService service, ITicketPagoService tickets) : ControllerBase
{
    // CAT al que está acotado el operador (null = admin, sin restricción)
    private CentroAcopio? FiltroCat() =>
        Enum.TryParse<CentroAcopio>(User.CatRestringido(), out var c) ? c : null;

    [HttpPost]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarPagoDto dto)
    {
        try
        {
            var resultado = await service.RegistrarAsync(dto, FiltroCat());
            return CreatedAtAction(nameof(Listar), null, resultado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> Listar(
        [FromQuery] int? productoraId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        var resultado = await service.ListarAsync(productoraId, desde, hasta, FiltroCat());
        return Ok(resultado);
    }

    /// <summary>
    /// Lotes por los que aún se le debe pagar a la productora: los que tienen
    /// entregas suyas y todavía no registran un pago suyo. Alimenta el
    /// selector del formulario de pago, para que un lote ya pagado no vuelva
    /// a ofrecerse.
    /// </summary>
    [HttpGet("lotes-pendientes/{productoraId:int}")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> LotesPendientes(int productoraId)
    {
        try
        {
            var resultado = await service.ListarLotesPendientesAsync(productoraId, FiltroCat());
            return Ok(resultado);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Ticket imprimible. Lo descarga tanto el CAT que lo emite como la planta
    /// que va a pagarlo, así que el rol de faenamiento entra aquí — pero sin
    /// acotarse por centro: la planta es única y atiende a los tres CAT.
    /// </summary>
    [HttpGet("{id:int}/ticket")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> Ticket(int id)
    {
        var filtro = FiltroCat();
        if (filtro is CentroAcopio cat)
        {
            var suyo = await service.EsDeCentroAsync(id, cat);
            // 404 y no 403: confirmar que existe ya sería filtrar el dato
            if (!suyo) return NotFound();
        }

        try
        {
            var pdf = await tickets.GenerarAsync(id);
            return File(pdf, "application/pdf", $"ticket-{id:D6}.pdf");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Bandeja de la planta. Deliberadamente distinta de `lotes-pendientes`,
    /// que es de la CAT y responde a otra pregunta: qué lotes le faltan por
    /// cobrar a una productora, no qué tickets le tocan pagar a la planta.
    /// Sin FiltroCat: la planta atiende a los tres centros.
    /// </summary>
    [HttpGet("por-pagar")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> PorPagar() =>
        Ok(await service.ListarPorPagarAsync());

    [HttpPost("{id:int}/pagar")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> Pagar(
        int id, [FromBody] RegistrarPagoEfectivoDto dto)
    {
        try
        {
            return Ok(await service.RegistrarPagoEfectivoAsync(id, dto));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (EvidenciaInvalidaException ex)
        {
            // 400: la captura subida viene mal
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (CuerpoInvalidoException ex)
        {
            // 400 también: un campo del cuerpo trae un valor inservible.
            // Se sabe leyendo la petición, sin consultar nada del servidor.
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (TransicionInvalidaException ex)
        {
            // 409: el cuerpo está bien, lo que no encaja es el momento
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpGet("{id:int}/cuyes-con-novedad")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> CuyesConNovedad(int id)
    {
        try
        {
            return Ok(await service.ListarCuyesConNovedadAsync(id));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
