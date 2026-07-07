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

        // A crédito: se difiere en N letras (mínimo 2) y el valor de cada
        // una lo calcula el servidor. Al contado no lleva letras.
        var esCredito = dto.MetodoPago.Trim()
            .Equals("Credito", StringComparison.OrdinalIgnoreCase);
        int? numeroLetras = null;
        decimal? valorPorLetra = null;

        if (esCredito)
        {
            if (dto.NumeroLetras is not int n || n < 2)
                throw new InvalidOperationException(
                    "Un pago a crédito debe diferirse en al menos 2 letras.");
            numeroLetras = n;
            valorPorLetra = Math.Round(dto.MontoUsd / n, 2);
        }

        var pago = new Pago
        {
            ProductoraId = dto.ProductoraId,
            LoteId = dto.LoteId,
            MontoUsd = dto.MontoUsd,
            FechaPago = DateTime.SpecifyKind(dto.FechaPago, DateTimeKind.Utc),
            MetodoPago = dto.MetodoPago.Trim(),
            NumeroLetras = numeroLetras,
            ValorPorLetra = valorPorLetra,
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
            .Take(300)
            .AsNoTracking()
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
        NumeroLetras: p.NumeroLetras,
        ValorPorLetra: p.ValorPorLetra,
        Responsable: p.Responsable,
        Observaciones: p.Observaciones
    );
}
