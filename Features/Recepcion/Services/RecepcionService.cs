using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Recepcion.Services;

public interface IRecepcionService
{
    Task<LoteResponseDto> RegistrarLoteAsync(RegistrarLoteDto dto);
    Task<LoteResponseDto?> ObtenerLotePorIdAsync(int id);
    Task<LoteResponseDto?> ObtenerLotePorCodigoAsync(string codigo);
    Task<IEnumerable<LoteResponseDto>> ListarLotesAsync(
        CentroAcopio? cat, EstadoLote? estado, DateTime? desde, DateTime? hasta);
    Task<SyncResultadoDto> SincronizarOfflineAsync(SyncLotesDto dto);
}

public class RecepcionService(AppDbContext db) : IRecepcionService
{
    // ── Registro individual de lote ───────────────────────────────────

    public async Task<LoteResponseDto> RegistrarLoteAsync(RegistrarLoteDto dto)
    {
        // 1. Verificar que la productora existe
        var productora = await db.Productoras.FindAsync(dto.ProductoraId)
            ?? throw new KeyNotFoundException(
                $"Productora con Id {dto.ProductoraId} no encontrada.");

        // 2. Generar código único de lote — SRS RF-103 / Apéndice 5.2
        var codigoLote = await GenerarCodigoLoteAsync(dto.CentroAcopio, dto.FechaRecepcion);

        var registroIndividual = dto.Cuyes.Count > 0;

        // 3. Aplicar reglas de negocio — SRS Apéndice 5.1.
        //    Con registro individual la evaluación es cuy por cuy y los
        //    totales del lote se derivan de los animales; el flujo agregado
        //    se mantiene para registros offline con el formato anterior.
        EstadoLote estado;
        List<Novedad> novedades;
        List<CuyRegistro> cuyes = [];

        if (registroIndividual)
            (estado, novedades, cuyes) = EvaluarCuyes(dto);
        else
            (estado, novedades) = EvaluarLote(dto);

        // 4. Crear lote
        var lote = new Lote
        {
            CodigoLote = codigoLote,
            ProductoraId = dto.ProductoraId,
            CentroAcopio = dto.CentroAcopio,
            FechaRecepcion = DateTime.SpecifyKind(dto.FechaRecepcion, DateTimeKind.Utc), // ← fix
            CantidadAnimales = registroIndividual ? cuyes.Count : dto.CantidadAnimales,
            PesoTotalGramos = registroIndividual
                ? cuyes.Sum(c => c.PesoGramos) : dto.PesoTotalGramos,
            Estado = estado,
            ResponsableRecepcion = dto.ResponsableRecepcion,
            Observaciones = dto.Observaciones,
            SignosClinicos = string.IsNullOrWhiteSpace(dto.SignosClinicos)
                ? null : dto.SignosClinicos.Trim(),
            SincronizadoOffline = dto.SincronizadoOffline,
            FechaSincronizacion = dto.SincronizadoOffline ? DateTime.UtcNow : null
        };

        db.Lotes.Add(lote);
        await db.SaveChangesAsync();

        // 5. Guardar el detalle individual por animal
        foreach (var cuy in cuyes)
        {
            cuy.LoteId = lote.Id;
            db.CuyRegistros.Add(cuy);
        }

        // 6. Guardar novedades detectadas automáticamente
        foreach (var novedad in novedades)
        {
            novedad.LoteId = lote.Id;
            db.Novedades.Add(novedad);
        }

        if (cuyes.Count > 0 || novedades.Count > 0)
            await db.SaveChangesAsync();

        return await MapearLoteAsync(lote.Id);
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<LoteResponseDto?> ObtenerLotePorIdAsync(int id)
    {
        var existe = await db.Lotes.AnyAsync(l => l.Id == id);
        return existe ? await MapearLoteAsync(id) : null;
    }

    public async Task<LoteResponseDto?> ObtenerLotePorCodigoAsync(string codigo)
    {
        var lote = await db.Lotes.FirstOrDefaultAsync(l => l.CodigoLote == codigo);
        return lote is null ? null : await MapearLoteAsync(lote.Id);
    }

    public async Task<IEnumerable<LoteResponseDto>> ListarLotesAsync(
        CentroAcopio? cat, EstadoLote? estado, DateTime? desde, DateTime? hasta)
    {
        var query = db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes)
            .AsQueryable();

        if (cat.HasValue)
            query = query.Where(l => l.CentroAcopio == cat.Value);

        if (estado.HasValue)
            query = query.Where(l => l.Estado == estado.Value);

        if (desde.HasValue)
            query = query.Where(l => l.FechaRecepcion >= desde.Value.ToUniversalTime());

        if (hasta.HasValue)
            query = query.Where(l => l.FechaRecepcion <= hasta.Value.ToUniversalTime());

        var lotes = await query
            .OrderByDescending(l => l.FechaRecepcion)
            .ToListAsync();

        return lotes.Select(MapearLote);
    }

    // ── Sincronización offline — RF-211 ───────────────────────────────

    public async Task<SyncResultadoDto> SincronizarOfflineAsync(SyncLotesDto dto)
    {
        var errores = new List<SyncErrorDto>();
        var guardados = 0;

        foreach (var loteDto in dto.Lotes)
        {
            try
            {
                // DESPUÉS (con clase):
                loteDto.SincronizadoOffline = true;
                loteDto.DispositivoId = dto.DispositivoId;
                var loteConSync = loteDto;

                await RegistrarLoteAsync(loteConSync);
                guardados++;
            }
            catch (Exception ex)
            {
                errores.Add(new SyncErrorDto(
                    DispositivoId: dto.DispositivoId,
                    CodigoLoteTemp: $"{loteDto.CentroAcopio}-{loteDto.FechaRecepcion:yyyyMMdd}-TEMP",
                    Motivo: ex.Message
                ));
            }
        }

        return new SyncResultadoDto(
            TotalRecibidos: dto.Lotes.Count,
            TotalGuardados: guardados,
            TotalConError: errores.Count,
            Errores: errores
        );
    }

    // ── Evaluación individual por cuy — SRS Apéndice 5.1 ─────────────
    // Cada animal se evalúa por separado; el estado del lote se deriva:
    // todos rechazados → Rechazado; alguna observación → ConNovedad.

    private static (EstadoLote estado, List<Novedad> novedades, List<CuyRegistro> cuyes)
        EvaluarCuyes(RegistrarLoteDto dto)
    {
        var novedades = new List<Novedad>();
        var cuyes = new List<CuyRegistro>();
        var numero = 0;

        foreach (var c in dto.Cuyes)
        {
            numero++;
            var motivos = new List<string>();
            var rechazado = false;

            if (c.PesoGramos < 850)
            {
                rechazado = true;
                motivos.Add($"peso {c.PesoGramos:F0}g bajo el mínimo (850g)");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.BajoPeso,
                    $"Peso {c.PesoGramos:F0}g por debajo del mínimo (850g). Animal rechazado.",
                    dto.ResponsableRecepcion, c.PesoGramos));
            }
            else if (c.PesoGramos < 875)
            {
                motivos.Add($"peso justo ({c.PesoGramos:F0}g)");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.BajoPeso,
                    $"Peso {c.PesoGramos:F0}g entre 850g–874g. Pasa con observación.",
                    dto.ResponsableRecepcion, c.PesoGramos));
            }
            else if (c.PesoGramos > 1300)
            {
                motivos.Add($"sobre el rango operativo ({c.PesoGramos:F0}g)");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.SobrePeso,
                    $"Peso {c.PesoGramos:F0}g sobre el rango operativo (máx. 1300g).",
                    dto.ResponsableRecepcion, c.PesoGramos));
            }

            if (c.ColorPelaje.Equals("Negro", StringComparison.OrdinalIgnoreCase))
            {
                motivos.Add("piel negra");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.ColorNoConforme,
                    "Piel completamente negra. No conforme para mercado formal.",
                    dto.ResponsableRecepcion, null));
            }

            if (c.EstadoOreja.Equals("Dura", StringComparison.OrdinalIgnoreCase))
            {
                motivos.Add("oreja dura");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.OrejaDura,
                    "Oreja dura: animal de edad avanzada.",
                    dto.ResponsableRecepcion, null));
            }

            if (!string.IsNullOrWhiteSpace(c.SignosClinicos))
            {
                motivos.Add($"signos clínicos: {c.SignosClinicos.Trim()}");
                novedades.Add(NovedadDeCuy(numero, TipoNovedad.SignosClinicos,
                    $"Condición sanitaria con observación: {c.SignosClinicos.Trim()}",
                    dto.ResponsableRecepcion, null));
            }

            cuyes.Add(new CuyRegistro
            {
                NumeroEnLote = numero,
                PesoGramos = c.PesoGramos,
                ColorPelaje = c.ColorPelaje,
                EstadoOreja = c.EstadoOreja,
                TamanoAnimal = c.TamanoAnimal,
                SignosClinicos = string.IsNullOrWhiteSpace(c.SignosClinicos)
                    ? null : c.SignosClinicos.Trim(),
                Estado = rechazado ? EstadoLote.Rechazado
                    : motivos.Count > 0 ? EstadoLote.ConNovedad
                    : EstadoLote.Aceptado,
                MotivoNovedad = motivos.Count > 0
                    ? string.Join("; ", motivos) : null
            });
        }

        // Condición de ayuno: aplica al lote completo (condición de entrega)
        if (!dto.EnAyunas)
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.SinAyuno,
                Descripcion = "Lote recibido sin ayuno. El peso registrado puede no ser el peso real.",
                RegistradoPor = dto.ResponsableRecepcion
            });
        }

        var estadoLote = cuyes.Count > 0 && cuyes.All(c => c.Estado == EstadoLote.Rechazado)
            ? EstadoLote.Rechazado
            : novedades.Count > 0 || cuyes.Any(c => c.Estado != EstadoLote.Aceptado)
                ? EstadoLote.ConNovedad
                : EstadoLote.Aceptado;

        return (estadoLote, novedades, cuyes);
    }

    private static Novedad NovedadDeCuy(
        int numero, TipoNovedad tipo, string descripcion,
        string registradoPor, decimal? peso) => new()
        {
            Tipo = tipo,
            Descripcion = $"Cuy #{numero}: {descripcion}",
            RegistradoPor = registradoPor,
            PesoRegistradoGramos = peso
        };

    // ── Lógica de negocio: evaluación del lote — SRS Apéndice 5.1 ────

    private static (EstadoLote estado, List<Novedad> novedades) EvaluarLote(
        RegistrarLoteDto dto)
    {
        var novedades = new List<Novedad>();
        var pesoPromedio = dto.CantidadAnimales > 0
            ? dto.PesoTotalGramos / dto.CantidadAnimales
            : 0;

        // Regla 1: Peso — RF-202
        if (pesoPromedio < 850)
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.BajoPeso,
                Descripcion = $"Peso promedio {pesoPromedio:F0}g por debajo del mínimo (850g). Lote rechazado.",
                RegistradoPor = dto.ResponsableRecepcion,
                PesoRegistradoGramos = pesoPromedio
            });
            return (EstadoLote.Rechazado, novedades);
        }

        if (pesoPromedio is >= 850 and < 875)
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.BajoPeso,
                Descripcion = $"Peso promedio {pesoPromedio:F0}g entre 850g–874g. Aceptado con novedad de bajo peso.",
                RegistradoPor = dto.ResponsableRecepcion,
                PesoRegistradoGramos = pesoPromedio
            });
        }

        // Regla 1b: tope del rango operativo (875g–1300g en pie)
        if (pesoPromedio > 1300)
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.SobrePeso,
                Descripcion = $"Peso promedio {pesoPromedio:F0}g sobre el rango operativo (máx. 1300g). Posible animal de edad avanzada.",
                RegistradoPor = dto.ResponsableRecepcion,
                PesoRegistradoGramos = pesoPromedio
            });
        }

        // Regla 2: Color — RF-203
        if (dto.ColorPelaje.Equals("Negro", StringComparison.OrdinalIgnoreCase))
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.ColorNoConforme,
                Descripcion = "Piel completamente negra. No conforme para mercado formal. Redirigir a venta local.",
                RegistradoPor = dto.ResponsableRecepcion
            });
        }

        // Regla 3: Edad (oreja) — RF-204
        if (dto.EstadoOreja.Equals("Dura", StringComparison.OrdinalIgnoreCase))
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.OrejaDura,
                Descripcion = "Oreja dura: animal de edad avanzada. Alta probabilidad de devolución por cliente.",
                RegistradoPor = dto.ResponsableRecepcion
            });
        }

        // Regla 4: Ayuno — RF-206
        if (!dto.EnAyunas)
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.SinAyuno,
                Descripcion = "Animal recibido sin estar en ayunas. El peso registrado puede no ser el peso real.",
                RegistradoPor = dto.ResponsableRecepcion
            });
        }

        // Regla 5: condición sanitaria visual (signos clínicos observados)
        if (!string.IsNullOrWhiteSpace(dto.SignosClinicos))
        {
            novedades.Add(new Novedad
            {
                Tipo = TipoNovedad.SignosClinicos,
                Descripcion = $"Condición sanitaria con observación: {dto.SignosClinicos.Trim()}",
                RegistradoPor = dto.ResponsableRecepcion
            });
        }

        var estado = novedades.Count > 0
            ? EstadoLote.ConNovedad
            : EstadoLote.Aceptado;

        return (estado, novedades);
    }

    // ── Generación de código de lote — SRS RF-103 / Apéndice 5.2 ─────
    // Formato: CAT-AAAAMMDD-SEC  ej: PAT-20260615-001

    private async Task<string> GenerarCodigoLoteAsync(
    CentroAcopio cat, DateTime fecha)
    {
        var prefijo = cat.ToString();
        var fechaUtc = DateTime.SpecifyKind(fecha, DateTimeKind.Utc); // ← fix
        var fechaStr = fechaUtc.ToString("yyyyMMdd");
        var baseStr = $"{prefijo}-{fechaStr}-";

        var conteo = await db.Lotes
            .CountAsync(l =>
                l.CodigoLote.StartsWith(baseStr) &&
                l.FechaRecepcion.Date == fechaUtc.Date);

        var secuencial = (conteo + 1).ToString("D3");
        return $"{baseStr}{secuencial}";
    }

    // ── Mapeo a DTOs ──────────────────────────────────────────────────

    private async Task<LoteResponseDto> MapearLoteAsync(int loteId)
    {
        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes)
            .FirstAsync(l => l.Id == loteId);

        return MapearLote(lote);
    }

    private static LoteResponseDto MapearLote(Lote lote) => new(
        Id: lote.Id,
        CodigoLote: lote.CodigoLote,
        ProductoraId: lote.ProductoraId,
        NombreProductora: lote.Productora?.NombreCompleto ?? string.Empty,
        CentroAcopio: lote.CentroAcopio.ToString(),
        FechaRecepcion: lote.FechaRecepcion,
        CantidadAnimales: lote.CantidadAnimales,
        PesoTotalGramos: lote.PesoTotalGramos,
        Estado: lote.Estado.ToString(),
        ResponsableRecepcion: lote.ResponsableRecepcion,
        Observaciones: lote.Observaciones,
        SincronizadoOffline: lote.SincronizadoOffline,
        Novedades: lote.Novedades
            .Select(n => new NovedadResponseDto(
                n.Id, n.Tipo.ToString(), n.Descripcion,
                n.PesoRegistradoGramos, n.FechaRegistro, n.RegistradoPor))
            .ToList(),
        Cuyes: lote.Cuyes
            .OrderBy(c => c.NumeroEnLote)
            .Select(c => new CuyRegistroResponseDto(
                c.Id, c.NumeroEnLote, c.PesoGramos, c.ColorPelaje,
                c.EstadoOreja, c.TamanoAnimal, c.SignosClinicos,
                c.Estado.ToString(), c.MotivoNovedad))
            .ToList()
    );
}