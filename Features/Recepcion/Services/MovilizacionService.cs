using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Recepcion.Services;

public interface IMovilizacionService
{
    Task<MovilizacionResponseDto> RegistrarAsync(string codigoLote, RegistrarMovilizacionDto dto);
    Task<MovilizacionResponseDto?> ConfirmarRecepcionAsync(int id, ConfirmarRecepcionPlantaDto dto);
    Task<IEnumerable<MovilizacionResponseDto>> ListarAsync(bool? pendientesRecepcion);
    Task<MovilizacionResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote);
}

/// <summary>
/// Eslabón de transporte CAT → planta. El registro de movilización
/// mantiene la cadena de trazabilidad durante el traslado, con los
/// datos del conductor y la declaración de tratamientos.
/// </summary>
public class MovilizacionService(AppDbContext db) : IMovilizacionService
{
    public async Task<MovilizacionResponseDto> RegistrarAsync(
        string codigoLote, RegistrarMovilizacionDto dto)
    {
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

        if (lote.Estado == Common.EstadoLote.Rechazado)
            throw new InvalidOperationException(
                $"El lote {codigoLote} está rechazado y no puede movilizarse a la planta.");

        var yaExiste = await db.Movilizaciones.AnyAsync(m => m.LoteId == lote.Id);
        if (yaExiste)
            throw new InvalidOperationException(
                $"El lote {codigoLote} ya tiene una movilización registrada.");

        if (dto.CantidadMovilizada > lote.CantidadAnimales)
            throw new InvalidOperationException(
                $"La cantidad movilizada ({dto.CantidadMovilizada}) supera la " +
                $"cantidad recibida en el lote ({lote.CantidadAnimales}).");

        var movilizacion = new Movilizacion
        {
            LoteId = lote.Id,
            FechaDespacho = DateTime.SpecifyKind(dto.FechaDespacho, DateTimeKind.Utc),
            Conductor = dto.Conductor.Trim(),
            CantidadMovilizada = dto.CantidadMovilizada,
            CondicionesTransporte = dto.CondicionesTransporte,
            TipoForraje = dto.TipoForraje,
            DiasRetiroMedicamentos = dto.DiasRetiroMedicamentos,
            ResponsableDespacho = dto.ResponsableDespacho.Trim(),
            Observaciones = dto.Observaciones
        };

        db.Movilizaciones.Add(movilizacion);
        await db.SaveChangesAsync();

        return Mapear(movilizacion, lote);
    }

    public async Task<MovilizacionResponseDto?> ConfirmarRecepcionAsync(
        int id, ConfirmarRecepcionPlantaDto dto)
    {
        var movilizacion = await db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movilizacion is null) return null;

        if (movilizacion.FechaRecepcionPlanta is not null)
            throw new InvalidOperationException(
                "Esta movilización ya tiene la recepción en planta confirmada.");

        movilizacion.FechaRecepcionPlanta =
            DateTime.SpecifyKind(dto.FechaRecepcionPlanta, DateTimeKind.Utc);
        movilizacion.RecibidoPor = dto.RecibidoPor.Trim();
        movilizacion.CondicionLlegada = dto.CondicionLlegada;

        await db.SaveChangesAsync();
        return Mapear(movilizacion, movilizacion.Lote);
    }

    public async Task<IEnumerable<MovilizacionResponseDto>> ListarAsync(
        bool? pendientesRecepcion)
    {
        var query = db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .AsQueryable();

        if (pendientesRecepcion == true)
            query = query.Where(m => m.FechaRecepcionPlanta == null);
        else if (pendientesRecepcion == false)
            query = query.Where(m => m.FechaRecepcionPlanta != null);

        var lista = await query
            .OrderByDescending(m => m.FechaDespacho)
            .Take(300)
            .AsNoTracking()
            .ToListAsync();

        return lista.Select(m => Mapear(m, m.Lote));
    }

    public async Task<MovilizacionResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote)
    {
        var movilizacion = await db.Movilizaciones
            .Include(m => m.Lote).ThenInclude(l => l.Productora)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Lote.CodigoLote == codigoLote);

        return movilizacion is null ? null : Mapear(movilizacion, movilizacion.Lote);
    }

    private static MovilizacionResponseDto Mapear(
        Movilizacion m, Productoras.Models.Lote lote) => new(
        Id: m.Id,
        LoteId: m.LoteId,
        CodigoLote: lote.CodigoLote,
        CentroAcopio: lote.CentroAcopio.ToString(),
        NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
        FechaDespacho: m.FechaDespacho,
        Conductor: m.Conductor,
        CantidadMovilizada: m.CantidadMovilizada,
        CondicionesTransporte: m.CondicionesTransporte,
        TipoForraje: m.TipoForraje,
        DiasRetiroMedicamentos: m.DiasRetiroMedicamentos,
        ResponsableDespacho: m.ResponsableDespacho,
        Observaciones: m.Observaciones,
        FechaRecepcionPlanta: m.FechaRecepcionPlanta,
        RecibidoPor: m.RecibidoPor,
        CondicionLlegada: m.CondicionLlegada
    );
}
