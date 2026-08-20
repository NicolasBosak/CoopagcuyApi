using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoopagcuyApi.Features.Recepcion.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecepcionController(
    IRecepcionService service,
    IGuiaMovilizacionService guiaService,
    IMovilizacionService movilizacionService,
    IValidator<RegistrarMovilizacionDto> movilizacionValidator) : ControllerBase
{
    // Un Operador de CAT solo puede registrar en su centro asignado.
    // El CAT del operador viaja como claim en el token JWT.
    private string? CatNoAutorizado(CentroAcopio cat)
    {
        if (!User.IsInRole("OperadorCAT")) return null;

        var catOperador = User.FindFirst("cat")?.Value;
        if (string.IsNullOrEmpty(catOperador))
            return "Tu usuario no tiene un centro de acopio asignado. " +
                   "Pide a un administrador que te lo asigne.";

        return catOperador != cat.ToString()
            ? $"Tu usuario solo puede registrar en el centro {catOperador}."
            : null;
    }

    // CAT asignado al operador (para filtrar lo que ve). null si no es
    // OperadorCAT o no tiene centro asignado.
    private CentroAcopio? CatDelOperador()
    {
        if (!User.IsInRole("OperadorCAT")) return null;
        var cat = User.FindFirst("cat")?.Value;
        return Enum.TryParse<CentroAcopio>(cat, out var c) ? c : null;
    }

    // ── Entregas por productora: armado de jaulas de hasta 15 ─────────

    /// <summary>
    /// Registra la entrega de una productora. Los cuyes se acumulan en la
    /// jaula abierta del CAT; al completar 15 la jaula se cierra como lote
    /// y el remanente abre una jaula nueva.
    /// </summary>
    [HttpPost("entregas")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> RegistrarEntrega([FromBody] RegistrarEntregaDto dto)
    {
        if (CatNoAutorizado(dto.CentroAcopio) is string motivo)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = motivo });

        // La ruta en línea sella la fecha en el servidor. Se fuerzan aquí para
        // que un cliente no pueda declararse offline y elegir su propia fecha:
        // solo /sync-entregas puede aportar el momento de captura.
        dto.SincronizadoOffline = false;
        dto.FechaCapturaOffline = null;

        try
        {
            var resultado = await service.RegistrarEntregaAsync(dto);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (EvidenciaInvalidaException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Obtiene la jaula abierta (en armado) del CAT, si existe.
    /// </summary>
    [HttpGet("lotes/abierto")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ObtenerLoteAbierto([FromQuery] CentroAcopio cat)
    {
        // El operador de CAT solo consulta la jaula de su propio centro
        var catEfectivo = CatDelOperador() ?? cat;
        var resultado = await service.ObtenerLoteAbiertoAsync(catEfectivo);
        return resultado is null ? NoContent() : Ok(resultado);
    }

    /// <summary>
    /// Cierra manualmente la jaula abierta aunque no llegue a 15,
    /// dejándola lista para movilización.
    /// </summary>
    [HttpPost("lotes/{codigoLote}/cerrar")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> CerrarLote(string codigoLote)
    {
        // El código de jaula empieza con el CAT (PAT-…, NIE-…)
        var prefijo = codigoLote.Split('-')[0];
        if (Enum.TryParse<CentroAcopio>(prefijo, out var catLote) &&
            CatNoAutorizado(catLote) is string motivo)
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = motivo });

        try
        {
            var resultado = await service.CerrarLoteAsync(codigoLote);
            return resultado is null ? NotFound() : Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    // Un OperadorCAT no debe consultar lotes de otros centros ni por Id
    // ni por código: las lecturas puntuales aplican el mismo filtro que
    // el listado
    private bool LoteFueraDeSuCat(DTOs.LoteResponseDto lote) =>
        CatDelOperador() is CentroAcopio catOperador
        && lote.CentroAcopio != catOperador.ToString();

    /// <summary>
    /// Obtiene un lote por su Id interno.
    /// </summary>
    [HttpGet("lotes/{id:int}")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ObtenerLotePorId(int id)
    {
        var resultado = await service.ObtenerLotePorIdAsync(id);
        if (resultado is null) return NotFound();

        if (LoteFueraDeSuCat(resultado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede consultar lotes de su centro."
            });

        return Ok(resultado);
    }

    /// <summary>
    /// Obtiene un lote por su código único (ej: PAT-20260615-001).
    /// Usado por el módulo QR y por el faenamiento.
    /// </summary>
    [HttpGet("lotes/codigo/{codigo}")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ObtenerLotePorCodigo(string codigo)
    {
        var resultado = await service.ObtenerLotePorCodigoAsync(codigo);
        if (resultado is null) return NotFound();

        if (LoteFueraDeSuCat(resultado))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede consultar lotes de su centro."
            });

        return Ok(resultado);
    }

    /// <summary>
    /// Lista lotes con filtros opcionales por CAT, estado y rango de fechas.
    /// </summary>
    [HttpGet("lotes")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ListarLotes(
        [FromQuery] CentroAcopio? cat,
        [FromQuery] EstadoLote? estado,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta)
    {
        // El operador de CAT solo ve los lotes de su centro
        var catEfectivo = CatDelOperador() ?? cat;
        var resultado = await service.ListarLotesAsync(catEfectivo, estado, desde, hasta);
        return Ok(resultado);
    }

    /// <summary>
    /// Genera la guía de movilización del lote en PDF — RF-210.
    /// Documento imprimible que acompaña el transporte del lote
    /// desde el CAT hasta la planta de faenamiento.
    /// </summary>
    [HttpGet("lotes/{codigoLote}/guia")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> GuiaMovilizacion(string codigoLote)
    {
        try
        {
            var bytes = await guiaService.GenerarGuiaPdfAsync(codigoLote);
            return File(bytes, "application/pdf", $"Guia-{codigoLote}.pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Endpoint de sincronización offline — RF-211.
    /// Recibe un batch de lotes capturados sin internet en el CAT
    /// y los registra en la base de datos central.
    /// </summary>
    [HttpPost("sync-entregas")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> SincronizarEntregas([FromBody] SyncEntregasDto dto)
    {
        foreach (var entrega in dto.Entregas)
        {
            if (CatNoAutorizado(entrega.CentroAcopio) is string motivo)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { mensaje = motivo });
        }

        var resultado = await service.SincronizarEntregasAsync(dto);
        return Ok(resultado);
    }

    // ── Movilización CAT → planta (eslabón transporte) ────────────────

    /// <summary>
    /// Registra la movilización del lote hacia la planta de faenamiento:
    /// conductor, condiciones de transporte y declaración de tratamientos.
    /// </summary>
    [HttpPost("lotes/{codigoLote}/movilizacion")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa")]
    public async Task<IActionResult> RegistrarMovilizacion(
        string codigoLote, [FromBody] RegistrarMovilizacionDto dto)
    {
        var validacion = await movilizacionValidator.ValidateAsync(dto);
        if (!validacion.IsValid)
            return BadRequest(new
            {
                mensaje = string.Join(" ",
                    validacion.Errors.Select(e => e.ErrorMessage))
            });

        try
        {
            var resultado = await movilizacionService.RegistrarAsync(codigoLote, dto);
            return CreatedAtAction(
                nameof(ObtenerMovilizacionPorLote),
                new { codigoLote },
                resultado);
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

    /// <summary>
    /// Confirma la llegada del lote a la planta de faenamiento.
    /// Cierra el eslabón documental CAT → transportista → planta.
    /// </summary>
    [HttpPatch("movilizaciones/{id:int}/recepcion")]
    [Authorize(Roles = "OperadorFaenamiento,AdminCooperativa")]
    public async Task<IActionResult> ConfirmarRecepcionPlanta(
        int id, [FromBody] ConfirmarRecepcionPlantaDto dto)
    {
        try
        {
            var resultado = await movilizacionService.ConfirmarRecepcionAsync(id, dto);
            return resultado is null ? NotFound() : Ok(resultado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista movilizaciones; con pendientes=true solo las que
    /// aún no confirman recepción en planta.
    /// </summary>
    [HttpGet("movilizaciones")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ListarMovilizaciones(
        [FromQuery] bool? pendientes)
    {
        var resultado = await movilizacionService.ListarAsync(pendientes);

        // El operador de CAT solo ve las movilizaciones de su centro
        if (CatDelOperador() is CentroAcopio cat)
            resultado = resultado
                .Where(m => m.CentroAcopio == cat.ToString())
                .ToList();

        return Ok(resultado);
    }

    /// <summary>
    /// Obtiene la movilización de un lote por su código.
    /// </summary>
    [HttpGet("lotes/{codigoLote}/movilizacion")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> ObtenerMovilizacionPorLote(string codigoLote)
    {
        var resultado = await movilizacionService.ObtenerPorCodigoLoteAsync(codigoLote);
        if (resultado is null) return NotFound();

        if (CatDelOperador() is CentroAcopio cat && resultado.CentroAcopio != cat.ToString())
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                mensaje = "Tu usuario solo puede consultar movilizaciones de su centro."
            });

        return Ok(resultado);
    }

    // ── Bandeja de vinculación ────────────────────────────────────────────
    // Entregas capturadas offline con cédula válida sin productora en el
    // centro. Solo la administración resuelve la vinculación.

    /// <summary>
    /// Lista las entregas pendientes de vincular a una productora.
    /// </summary>
    [HttpGet("vinculaciones")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ListarVinculaciones()
    {
        var resultado = await service.ListarVinculacionesAsync(filtroCat: null);
        return Ok(resultado);
    }

    /// <summary>
    /// Asigna una entrega pendiente a una productora y la registra en su
    /// jaula conservando la fecha real de captura.
    /// </summary>
    [HttpPost("vinculaciones/{id:int}/resolver")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> ResolverVinculacion(
        int id, [FromBody] ResolverVinculacionDto dto)
    {
        var resueltaPor = User.FindFirst("cedula")?.Value ?? "admin";
        try
        {
            var resultado = await service.ResolverVinculacionAsync(
                id, dto.ProductoraId, resueltaPor);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (EvidenciaInvalidaException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Descarta una entrega pendiente (cédula errada sin productora real).
    /// </summary>
    [HttpDelete("vinculaciones/{id:int}")]
    [Authorize(Roles = "AdminCooperativa,AdminTecnico")]
    public async Task<IActionResult> DescartarVinculacion(int id)
    {
        var resueltaPor = User.FindFirst("cedula")?.Value ?? "admin";
        var ok = await service.DescartarVinculacionAsync(id, resueltaPor);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Evidencia fotográfica de una novedad clínica. Se sirve a través del API
    /// y no por URL directa al Blob porque el contenedor es privado: es una
    /// foto de defectos atribuida a un proveedor, no un QR público.
    /// </summary>
    // OperadorFaenamiento incluido: es quien tiene el animal delante al
    // procesarlo, y el asistente de faenamiento muestra la evidencia junto
    // al cuy. El filtro por centro de abajo no le aplica —CatDelOperador()
    // solo devuelve un CAT para OperadorCAT— y así debe ser: la planta
    // recibe jaulas de los cinco centros.
    [HttpGet("novedades/{id:int}/foto")]
    [Authorize(Roles = "OperadorCAT,AdminCooperativa,OperadorFaenamiento")]
    public async Task<IActionResult> FotoDeNovedad(int id)
    {
        // Mismo filtro de centro que los otros endpoints de lectura puntual
        // (ver CatDelOperador). Un OperadorCAT que pida la foto de otro
        // centro recibe 404, no 403: un 403 confirmaría que ese id existe.
        var bytes = await service.ObtenerFotoNovedadAsync(id, CatDelOperador());

        return bytes is null
            ? NotFound(new { mensaje = "La evidencia no existe o ya caducó." })
            : File(bytes, "image/jpeg");
    }
}