using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.DTOs;
using CoopagcuyApi.Features.Faenamiento.Models;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Faenamiento.Services;

public interface IFaenamientoService
{
    Task<FaenamientoResponseDto> RegistrarFaenamientoAsync(RegistrarFaenamientoDto dto);
    Task<FaenamientoResponseDto?> ObtenerPorLoteIdAsync(int loteId);
    Task<FaenamientoResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote);
    Task<IEnumerable<FaenamientoResponseDto>> ListarAsync(DateTime? desde, DateTime? hasta);
    Task<DespachoResponseDto> RegistrarDespachoAsync(RegistrarDespachoDto dto);
    Task<IEnumerable<DespachoResponseDto>> ListarDespachosPorLoteAsync(int loteId);
    Task<DevolucionResponseDto> RegistrarDevolucionAsync(RegistrarDevolucionDto dto);
    Task<IEnumerable<DevolucionResponseDto>> ListarDevolucionesAsync(
        DateTime? desde, DateTime? hasta, int? productoraId);
    Task<InkJetCodigoDto?> ObtenerDatosInkJetAsync(string codigoLote);
}

public class FaenamientoService(AppDbContext db) : IFaenamientoService
{
    // ── Registro de faenamiento ───────────────────────────────────────

    public async Task<FaenamientoResponseDto> RegistrarFaenamientoAsync(
        RegistrarFaenamientoDto dto)
    {
        // 1. Verificar que el lote existe y está aceptado o con novedad
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Faenamiento)
            .FirstOrDefaultAsync(l => l.Id == dto.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {dto.LoteId} no encontrado.");

        if (lote.Estado == EstadoLote.Rechazado)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} está rechazado y no puede faenarse.");

        if (lote.Faenamiento is not null)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} ya tiene un registro de faenamiento.");

        // 2. Validar decomisos: faenados + decomisados no superan el lote
        if (dto.UnidadesDecomisadas < 0)
            throw new InvalidOperationException(
                "Las unidades decomisadas no pueden ser negativas.");

        if (dto.UnidadesFaenadas + dto.UnidadesDecomisadas > lote.CantidadAnimales)
            throw new InvalidOperationException(
                $"Faenados ({dto.UnidadesFaenadas}) más decomisados " +
                $"({dto.UnidadesDecomisadas}) superan los animales del lote " +
                $"({lote.CantidadAnimales}).");

        // 3. Crear registro de faenamiento
        var faenamiento = new RegistroFaenamiento
        {
            LoteId = dto.LoteId,
            FechaFaenamiento = DateTime.SpecifyKind(
                                    dto.FechaFaenamiento, DateTimeKind.Utc),
            OperarioResponsable = dto.OperarioResponsable,
            UnidadesFaenadas = dto.UnidadesFaenadas,
            PesoTotalCanalGramos = dto.PesoTotalCanalGramos,
            TemperaturaAlmacenamiento = dto.TemperaturaAlmacenamiento,
            EstadoCanal = dto.EstadoCanal,
            Observaciones = dto.Observaciones,
            UnidadesDecomisadas = dto.UnidadesDecomisadas,
            MotivoDecomiso = dto.MotivoDecomiso,
            TiempoLavadoMinutos = dto.TiempoLavadoMinutos,
            PresentacionEmpaque = dto.PresentacionEmpaque,
            FechaIngresoFrio = dto.FechaIngresoFrio is DateTime fi
                ? DateTime.SpecifyKind(fi, DateTimeKind.Utc) : null,
            FechaSalidaFrio = dto.FechaSalidaFrio is DateTime fs
                ? DateTime.SpecifyKind(fs, DateTimeKind.Utc) : null
        };

        db.Faenamientos.Add(faenamiento);
        await db.SaveChangesAsync();

        return MapearFaenamiento(faenamiento, lote);
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<FaenamientoResponseDto?> ObtenerPorLoteIdAsync(int loteId)
    {
        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .FirstOrDefaultAsync(f => f.LoteId == loteId);

        return faenamiento is null
            ? null
            : MapearFaenamiento(faenamiento, faenamiento.Lote);
    }

    public async Task<FaenamientoResponseDto?> ObtenerPorCodigoLoteAsync(string codigoLote)
    {
        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .FirstOrDefaultAsync(f => f.Lote.CodigoLote == codigoLote);

        return faenamiento is null
            ? null
            : MapearFaenamiento(faenamiento, faenamiento.Lote);
    }

    public async Task<IEnumerable<FaenamientoResponseDto>> ListarAsync(
        DateTime? desde, DateTime? hasta)
    {
        var query = db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(f =>
                f.FechaFaenamiento >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(f =>
                f.FechaFaenamiento <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        var lista = await query
            .OrderByDescending(f => f.FechaFaenamiento)
            .ToListAsync();

        return lista.Select(f => MapearFaenamiento(f, f.Lote));
    }

    // ── Registro de despacho ──────────────────────────────────────────

    public async Task<DespachoResponseDto> RegistrarDespachoAsync(
        RegistrarDespachoDto dto)
    {
        // Verificar que el lote existe y tiene faenamiento registrado
        var lote = await db.Lotes
            .Include(l => l.Faenamiento)
            .FirstOrDefaultAsync(l => l.Id == dto.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {dto.LoteId} no encontrado.");

        if (lote.Faenamiento is null)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} no tiene faenamiento registrado. " +
                "Registre el faenamiento antes de despachar.");

        var despacho = new Despacho
        {
            LoteId = dto.LoteId,
            ClienteDestino = dto.ClienteDestino,
            FechaDespacho = DateTime.SpecifyKind(dto.FechaDespacho, DateTimeKind.Utc),
            CantidadUnidades = dto.CantidadUnidades,
            Responsable = dto.Responsable,
            Transporte = dto.Transporte,
            Observaciones = dto.Observaciones
        };

        db.Despachos.Add(despacho);
        await db.SaveChangesAsync();

        return new DespachoResponseDto(
            Id: despacho.Id,
            LoteId: despacho.LoteId,
            CodigoLote: lote.CodigoLote,
            ClienteDestino: despacho.ClienteDestino,
            FechaDespacho: despacho.FechaDespacho,
            CantidadUnidades: despacho.CantidadUnidades,
            Responsable: despacho.Responsable,
            Transporte: despacho.Transporte,
            Observaciones: despacho.Observaciones
        );
    }

    public async Task<IEnumerable<DespachoResponseDto>> ListarDespachosPorLoteAsync(
        int loteId)
    {
        var lote = await db.Lotes.FindAsync(loteId)
            ?? throw new KeyNotFoundException($"Lote con Id {loteId} no encontrado.");

        return await db.Despachos
            .Where(d => d.LoteId == loteId)
            .OrderByDescending(d => d.FechaDespacho)
            .Select(d => new DespachoResponseDto(
                d.Id, d.LoteId, lote.CodigoLote, d.ClienteDestino,
                d.FechaDespacho, d.CantidadUnidades, d.Responsable,
                d.Transporte, d.Observaciones))
            .ToListAsync();
    }

    // ── Devoluciones de clientes — RF-307 ─────────────────────────────

    public async Task<DevolucionResponseDto> RegistrarDevolucionAsync(
        RegistrarDevolucionDto dto)
    {
        // La devolución solo aplica a producto despachado: el lote debe
        // tener faenamiento registrado.
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Faenamiento)
            .FirstOrDefaultAsync(l => l.Id == dto.LoteId)
            ?? throw new KeyNotFoundException(
                $"Lote con Id {dto.LoteId} no encontrado.");

        if (lote.Faenamiento is null)
            throw new InvalidOperationException(
                $"El lote {lote.CodigoLote} no tiene faenamiento registrado. " +
                "No es posible registrar una devolución.");

        var devolucion = new Devolucion
        {
            LoteId = dto.LoteId,
            ClienteDevuelve = dto.ClienteDevuelve,
            FechaDevolucion = DateTime.SpecifyKind(dto.FechaDevolucion, DateTimeKind.Utc),
            CantidadUnidades = dto.CantidadUnidades,
            Motivo = dto.Motivo,
            Responsable = dto.Responsable,
            Observaciones = dto.Observaciones
        };

        db.Devoluciones.Add(devolucion);
        await db.SaveChangesAsync();

        return MapearDevolucion(devolucion, lote);
    }

    public async Task<IEnumerable<DevolucionResponseDto>> ListarDevolucionesAsync(
        DateTime? desde, DateTime? hasta, int? productoraId)
    {
        var query = db.Devoluciones
            .Include(d => d.Lote).ThenInclude(l => l.Productora)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion >= DateTime.SpecifyKind(desde.Value, DateTimeKind.Utc));

        if (hasta.HasValue)
            query = query.Where(d =>
                d.FechaDevolucion <= DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc));

        if (productoraId.HasValue)
            query = query.Where(d => d.Lote.ProductoraId == productoraId.Value);

        var lista = await query
            .OrderByDescending(d => d.FechaDevolucion)
            .ToListAsync();

        return lista.Select(d => MapearDevolucion(d, d.Lote));
    }

    private static DevolucionResponseDto MapearDevolucion(Devolucion d, Lote lote) => new(
        Id: d.Id,
        LoteId: d.LoteId,
        CodigoLote: lote.CodigoLote,
        NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
        Comunidad: lote.Productora?.Comunidad ?? string.Empty,
        ClienteDevuelve: d.ClienteDevuelve,
        FechaDevolucion: d.FechaDevolucion,
        CantidadUnidades: d.CantidadUnidades,
        Motivo: d.Motivo,
        Responsable: d.Responsable,
        Observaciones: d.Observaciones
    );

    // ── Datos para codificador Ink Jet — RF-305 ───────────────────────

    public async Task<InkJetCodigoDto?> ObtenerDatosInkJetAsync(string codigoLote)
    {
        var faenamiento = await db.Faenamientos
            .Include(f => f.Lote).ThenInclude(l => l.Productora)
            .FirstOrDefaultAsync(f => f.Lote.CodigoLote == codigoLote);

        if (faenamiento is null) return null;

        var promedio = faenamiento.UnidadesFaenadas > 0
            ? faenamiento.PesoTotalCanalGramos / faenamiento.UnidadesFaenadas
            : 0;

        return new InkJetCodigoDto(
            CodigoLote: faenamiento.Lote.CodigoLote,
            FechaFaenamiento: faenamiento.FechaFaenamiento.ToString("dd/MM/yyyy"),
            FechaVencimiento: faenamiento.FechaFaenamiento.AddDays(5)
                                         .ToString("dd/MM/yyyy"),
            ComunidadOrigen: faenamiento.Lote.Productora.Comunidad,
            NombreProductora: faenamiento.Lote.Productora.NombreCompleto,
            UnidadesFaenadas: faenamiento.UnidadesFaenadas,
            PesoPromedioCanalGramos: Math.Round(promedio, 0)
        );
    }

    // ── Mapeo ─────────────────────────────────────────────────────────

    private static FaenamientoResponseDto MapearFaenamiento(RegistroFaenamiento f, Lote lote)
        {
        var promedio = f.UnidadesFaenadas > 0
            ? f.PesoTotalCanalGramos / f.UnidadesFaenadas
            : 0;

        return new FaenamientoResponseDto(
            Id: f.Id,
            LoteId: f.LoteId,
            CodigoLote: lote.CodigoLote,
            NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
            ComunidadOrigen: lote.Productora?.Comunidad ?? string.Empty,
            FechaFaenamiento: f.FechaFaenamiento,
            OperarioResponsable: f.OperarioResponsable,
            UnidadesFaenadas: f.UnidadesFaenadas,
            PesoTotalCanalGramos: f.PesoTotalCanalGramos,
            PesoPromedioCanalGramos: Math.Round(promedio, 2),
            TemperaturaAlmacenamiento: f.TemperaturaAlmacenamiento,
            EstadoCanal: f.EstadoCanal.ToString(),
            Observaciones: f.Observaciones,
            UnidadesDecomisadas: f.UnidadesDecomisadas,
            MotivoDecomiso: f.MotivoDecomiso,
            TiempoLavadoMinutos: f.TiempoLavadoMinutos,
            PresentacionEmpaque: f.PresentacionEmpaque,
            FechaIngresoFrio: f.FechaIngresoFrio,
            FechaSalidaFrio: f.FechaSalidaFrio
        );
    }
}