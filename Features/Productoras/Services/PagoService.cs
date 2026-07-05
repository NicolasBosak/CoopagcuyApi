using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Productoras.Services;

public interface IPagoService
{
    Task<PagoResponseDto> RegistrarAsync(RegistrarPagoDto dto);
    Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta);
}

/// <summary>
/// Pagos a productoras por entregas en el CAT. Digitaliza el registro
/// de pagos que hoy se lleva en cuaderno manual (brecha del diagnóstico).
/// </summary>
public class PagoService(AppDbContext db) : IPagoService
{
    public async Task<PagoResponseDto> RegistrarAsync(RegistrarPagoDto dto)
    {
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        Lote? lote = null;
        if (dto.LoteId is int loteId)
        {
            lote = await db.Lotes.FindAsync(loteId)
                ?? throw new KeyNotFoundException(
                    $"Lote con Id {loteId} no encontrado.");

            // La jaula es multi-productora: el pago es válido si la
            // productora entregó cuyes en ese lote (Lote.ProductoraId es
            // solo la referencia histórica de quien abrió la jaula)
            var participo = lote.ProductoraId == dto.ProductoraId
                || await db.CuyRegistros.AnyAsync(c =>
                    c.LoteId == loteId && c.ProductoraId == dto.ProductoraId);

            if (!participo)
                throw new InvalidOperationException(
                    "La productora no registra entregas en ese lote.");
        }

        if (dto.MontoUsd <= 0)
            throw new InvalidOperationException(
                "El monto del pago debe ser mayor a cero.");

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = dto.LoteId,
            MontoUsd = dto.MontoUsd,
            FechaPago = DateTime.SpecifyKind(dto.FechaPago, DateTimeKind.Utc),
            MetodoPago = dto.MetodoPago.Trim(),
            Responsable = dto.Responsable.Trim(),
            Observaciones = dto.Observaciones
        };

        db.Pagos.Add(pago);
        await db.SaveChangesAsync();

        return Mapear(pago, productora.NombreCompleto, lote?.CodigoLote);
    }

    public async Task<IEnumerable<PagoResponseDto>> ListarAsync(
        int? productoraId, DateTime? desde, DateTime? hasta)
    {
        var query = db.Pagos
            .Include(p => p.Productora)
            .Include(p => p.Lote)
            .AsQueryable();

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
            .ToListAsync();

        return lista.Select(p => Mapear(
            p, p.Productora.NombreCompleto, p.Lote?.CodigoLote));
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
        Responsable: p.Responsable,
        Observaciones: p.Observaciones
    );
}
