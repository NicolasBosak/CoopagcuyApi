using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Pagos.DTOs;
using CoopagcuyApi.Features.Pagos.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Pagos.Services;

public interface IPagoService
{
    // filtroCat: si viene un CAT (operador acotado), el servicio solo opera
    // sobre productoras de ese centro. null = sin restricción (administrador).
    Task<PagoResponseDto> RegistrarAsync(RegistrarPagoDto dto, CentroAcopio? filtroCat);
    Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta, CentroAcopio? filtroCat);
    Task<IEnumerable<LotePendientePagoDto>> ListarLotesPendientesAsync(
        int productoraId, CentroAcopio? filtroCat);

    /// Si el pago pertenece a una productora de ese centro. Sirve para
    /// responder 404 sin revelar la existencia del recurso.
    Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat);

    /// Tickets pendientes de pago, de TODOS los centros.
    Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync();

    /// Cuyes de esa productora en ese lote que traen novedad del CAT.
    Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(int pagoId);
}

/// <summary>
/// Pagos a productoras por entregas en el CAT. Digitaliza el registro
/// de pagos que hoy se lleva en cuaderno manual (brecha del diagnóstico).
/// </summary>
public class PagoService(AppDbContext db) : IPagoService
{
    public async Task<PagoResponseDto> RegistrarAsync(
        RegistrarPagoDto dto, CentroAcopio? filtroCat)
    {
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        // Un operador solo paga a productoras de su propio centro
        if (filtroCat is CentroAcopio cat && productora.CatAsignado != cat)
            throw new UnauthorizedAccessException(
                "Tu usuario solo puede registrar pagos de productoras de su centro.");

        // El ticket es por los cuyes de un lote concreto. Sin lote no hay nada
        // que imprimir ni novedades que trazar después.
        if (dto.LoteId is not int loteId)
            throw new InvalidOperationException(
                "El pago debe corresponder a un lote.");

        var lote = await db.Lotes.FindAsync(loteId)
            ?? throw new KeyNotFoundException($"Lote con Id {loteId} no encontrado.");

        // La jaula es multi-productora: el pago es válido si la productora
        // entregó cuyes en ese lote (Lote.ProductoraId es solo la referencia
        // histórica de quien abrió la jaula)
        var participo = lote.ProductoraId == dto.ProductoraId
            || await db.CuyRegistros.AnyAsync(c =>
                c.LoteId == loteId && c.ProductoraId == dto.ProductoraId);

        if (!participo)
            throw new InvalidOperationException(
                "La productora no registra entregas en ese lote.");

        if (dto.MontoUsd <= 0)
            throw new InvalidOperationException(
                "El monto del pago debe ser mayor a cero.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = loteId,
            MontoUsd = dto.MontoUsd,
            FechaPago = DateTime.UtcNow,
            // Fijado por el servidor, no por el cliente: desde el paso a
            // transferencia única no hay nada que elegir.
            MetodoPago = "Transferencia",
            Estado = EstadoPago.Pendiente,
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        return Mapear(pago, productora.NombreCompleto, lote.CodigoLote);
    }

    public async Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta, CentroAcopio? filtroCat)
    {
        var query = db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .AsQueryable();

        // Operador acotado: solo pagos de productoras de su centro
        if (filtroCat is CentroAcopio cat)
            query = query.Where(p => p.Productora.CatAsignado == cat);

        if (productoraId.HasValue)
            query = query.Where(p => p.ProductoraId == productoraId.Value);

        if (desde.HasValue)
            query = query.Where(p =>
                p.FechaPago >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(p =>
                p.FechaPago <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        var lista = await query
            .OrderByDescending(p => p.FechaPago)
            .Take(300)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(p => Mapear(
            p, p.Productora.NombreCompleto, p.Lote?.CodigoLote));
    }

    /// <summary>
    /// Lotes por los que aún se le debe pagar a esta productora.
    ///
    /// El pago es por par (productora, lote): una jaula reúne cuyes de varias
    /// productoras, así que pagarle a una no salda a las demás. Por eso el
    /// lote se excluye solo si YA existe un pago de ESA productora por ESE
    /// lote, y no en cuanto alguien cobre la jaula.
    ///
    /// La pertenencia se resuelve con la misma regla que valida RegistrarAsync
    /// (entregó cuyes, o abrió la jaula): si la lista mostrara otra cosa,
    /// ofrecería lotes que el servidor luego rechaza, o escondería lotes que sí
    /// acepta.
    /// </summary>
    public async Task<IEnumerable<LotePendientePagoDto>> ListarLotesPendientesAsync(
        int productoraId, CentroAcopio? filtroCat)
    {
        // Un operador no puede sondear los lotes pendientes de productoras de
        // otro centro pasando su Id a mano
        if (filtroCat is CentroAcopio cat)
        {
            var productora = await db.Productoras.FindAsync(productoraId);
            if (productora is null || productora.CatAsignado != cat)
                throw new UnauthorizedAccessException(
                    "Tu usuario solo puede consultar productoras de su centro.");
        }

        var pagados = db.Pagos
            .Where(p => p.ProductoraId == productoraId && p.LoteId != null)
            .Select(p => p.LoteId!.Value);

        return await db.Lotes
            .Where(l =>
                (l.ProductoraId == productoraId
                 || l.Cuyes.Any(c => c.ProductoraId == productoraId))
                && !pagados.Contains(l.Id))
            .OrderByDescending(l => l.FechaRecepcion)
            .Select(l => new LotePendientePagoDto(
                l.Id,
                l.CodigoLote,
                l.CentroAcopio.ToString(),
                l.FechaRecepcion,
                // Lo que aportó ESTA productora, no el total de la jaula:
                // es la base sobre la que se le paga
                l.Cuyes.Count(c => c.ProductoraId == productoraId),
                l.Cuyes
                    .Where(c => c.ProductoraId == productoraId)
                    .Sum(c => (decimal?)c.PesoGramos) ?? 0))
            .AsNoTracking()
            .ToListAsync();
    }

    public Task<bool> EsDeCentroAsync(int pagoId, CentroAcopio cat) =>
        db.Pagos.AnyAsync(p => p.Id == pagoId && p.Productora.CatAsignado == cat);

    public async Task<IEnumerable<TicketPorPagarDto>> ListarPorPagarAsync() =>
        await db.Pagos
            .Where(p => p.Estado == EstadoPago.Pendiente && p.LoteId != null)
            .OrderBy(p => p.FechaPago)
            .Select(p => new TicketPorPagarDto(
                p.Id,
                p.ProductoraId,
                p.Productora.NombreCompleto,
                p.Productora.Cedula,
                p.LoteId!.Value,
                p.Lote!.CodigoLote,
                p.Lote.CentroAcopio.ToString(),
                p.Lote.FechaRecepcion,
                // Aporte de ESTA productora, no el total de la jaula
                p.Lote.Cuyes.Count(c => c.ProductoraId == p.ProductoraId),
                p.MontoUsd,
                p.FechaPago))
            .AsNoTracking()
            .ToListAsync();

    public async Task<IEnumerable<CuyConNovedadDto>> ListarCuyesConNovedadAsync(
        int pagoId)
    {
        var pago = await db.Pagos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        if (pago.LoteId is not int loteId)
            return [];

        var ahora = DateTime.UtcNow;

        // Se parte de Novedades y no de CuyRegistros porque lo que la planta
        // necesita es el Id de la NOVEDAD: es lo que va a citar al descontar.
        return await db.Novedades
            .Where(n => n.LoteId == loteId
                // Deliberadamente redundante con la comparación de
                // ProductoraId de abajo: en SQL, si CuyRegistro es null (por
                // el LEFT JOIN), TODAS sus columnas son null, y comparar una
                // columna null contra un valor nunca da verdadero — esa fila
                // ya queda excluida sin este chequeo. Se deja igual porque
                // dice la regla en voz alta ("una novedad de la entrega, sin
                // animal, jamás es descontable") en vez de obligar a
                // deducirla de la semántica de NULL de SQL. Por eso mismo
                // ninguna prueba puede pinnearla: no hay dato que haga que su
                // presencia o ausencia cambie el resultado. Si una prueba no
                // la detecta al borrarla, NO es una prueba floja — no la
                // busques ni la borres pensando que es código muerto.
                && n.CuyRegistro != null
                && n.CuyRegistro.ProductoraId == pago.ProductoraId)
            .OrderBy(n => n.CuyRegistro!.NumeroEnLote)
            .Select(n => new CuyConNovedadDto(
                n.CuyRegistroId!.Value,
                n.CuyRegistro!.NumeroEnLote,
                n.CuyRegistro.PesoGramos,
                n.Id,
                n.Tipo.ToString(),
                n.Descripcion,
                n.FotoUrl != null && n.FotoExpiraEn > ahora))
            .AsNoTracking()
            .ToListAsync();
    }

    private static PagoResponseDto Mapear(
        Pago p, string nombreProductora, string? codigoLote) => new(
        Id: p.Id,
        ProductoraId: p.ProductoraId,
        NombreProductora: nombreProductora,
        LoteId: p.LoteId,
        CodigoLote: codigoLote,
        MontoUsd: p.MontoUsd,
        FechaPago: p.FechaPago,
        MetodoPago: p.MetodoPago,
        Estado: p.Estado.ToString(),
        MontoPagadoUsd: p.MontoPagadoUsd,
        FechaPagoEfectivo: p.FechaPagoEfectivo,
        PagadoPor: p.PagadoPor,
        TieneComprobante: p.ComprobanteUrl != null,
        FechaVerificacion: p.FechaVerificacion,
        VerificadoPor: p.VerificadoPor,
        Responsable: p.Responsable,
        Observaciones: p.Observaciones
    );
}
