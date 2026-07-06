using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Exceptions;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Features.Recepcion.DTOs;
using CoopagcuyApi.Features.Recepcion.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Recepcion.Services;

public interface IRecepcionService
{
    Task<EntregaResultadoDto> RegistrarEntregaAsync(RegistrarEntregaDto dto);
    Task<LoteResponseDto?> ObtenerLoteAbiertoAsync(CentroAcopio cat);
    Task<LoteResponseDto?> CerrarLoteAsync(string codigoLote);
    Task<LoteResponseDto?> ObtenerLotePorIdAsync(int id);
    Task<LoteResponseDto?> ObtenerLotePorCodigoAsync(string codigo);
    Task<IEnumerable<LoteResponseDto>> ListarLotesAsync(
        CentroAcopio? cat, EstadoLote? estado, DateTime? desde, DateTime? hasta);
    Task<SyncResultadoDto> SincronizarEntregasAsync(SyncEntregasDto dto);
}

public class RecepcionService(AppDbContext db) : IRecepcionService
{
    // Capacidad máxima de la jaula de transporte — SRS RF-104
    private const int CapacidadJaula = 20;

    // Tope del listado general: se carga lo más reciente y el histórico
    // se consulta con los filtros de fecha (evita degradar con la historia)
    private const int MaxLotesListado = 300;

    // ── Entregas por productora: la jaula se arma acumulando ─────────
    // Cada productora entrega los cuyes que quiera; se suman a la jaula
    // abierta del CAT hasta completar 20. Al llenarse, la jaula se cierra
    // y el remanente de la entrega abre una jaula nueva.

    public async Task<EntregaResultadoDto> RegistrarEntregaAsync(RegistrarEntregaDto dto)
    {
        if (dto.Cuyes.Count == 0)
            throw new InvalidOperationException(
                "La entrega debe incluir al menos un cuy.");

        // Toda la entrega es atómica y las entregas del MISMO CAT se
        // serializan con un advisory lock de PostgreSQL: así no pueden
        // crearse dos jaulas abiertas, sobrellenarse una jaula ni chocar
        // dos códigos de lote generados a la vez. CATs distintos no se
        // bloquean entre sí. El lock se libera solo al terminar la
        // transacción.
        var estrategia = db.Database.CreateExecutionStrategy();

        // El delegado reintentable termina EXACTAMENTE en el commit: el
        // mapeo de la respuesta queda fuera para que un fallo transitorio
        // posterior nunca re-ejecute (y duplique) la entrega
        var (idsAfectados, seCompleto) = await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaccion = await db.Database.BeginTransactionAsync();

            var claveLock = $"entrega-{dto.CentroAcopio}";
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({claveLock}))");

            // Idempotencia del sync offline: si esta entrega ya se procesó
            // (reintento tras corte de red), no se registra dos veces.
            // La verificación ocurre bajo el lock: un reintento concurrente
            // espera aquí hasta que el primero confirme.
            if (dto.IdCliente is not null && dto.DispositivoId is not null)
            {
                var yaProcesada = await db.SyncEntregasProcesadas.AnyAsync(s =>
                    s.DispositivoId == dto.DispositivoId &&
                    s.IdCliente == dto.IdCliente);
                if (yaProcesada)
                    throw new EntregaDuplicadaException(dto.IdCliente);
            }

            var productora = await db.Productoras.FindAsync(dto.ProductoraId)
                ?? throw new KeyNotFoundException(
                    $"Productora con Id {dto.ProductoraId} no encontrada.");

            var fechaUtc = DateTime.SpecifyKind(dto.FechaEntrega, DateTimeKind.Utc);
            var pendientes = new Queue<CuyRegistroDto>(dto.Cuyes);
            var lotesAfectados = new List<Lote>();
            var seCompletoJaula = false;

            while (pendientes.Count > 0)
            {
                var lote = await ObtenerOCrearJaulaAbiertaAsync(dto, fechaUtc);
                if (!lotesAfectados.Contains(lote))
                    lotesAfectados.Add(lote);

                var espacio = CapacidadJaula - lote.CantidadAnimales;
                var aTomar = Math.Min(espacio, pendientes.Count);

                for (var i = 0; i < aTomar; i++)
                {
                    var cuyDto = pendientes.Dequeue();
                    var numero = lote.CantidadAnimales + 1;

                    var (cuy, novedades) = EvaluarCuyIndividual(
                        cuyDto, numero, dto.ResponsableRecepcion);

                    cuy.LoteId = lote.Id;
                    cuy.ProductoraId = dto.ProductoraId;
                    db.CuyRegistros.Add(cuy);
                    lote.Cuyes.Add(cuy);

                    foreach (var novedad in novedades)
                    {
                        novedad.LoteId = lote.Id;
                        novedad.Descripcion =
                            $"{novedad.Descripcion} (entregado por {productora.NombreCompleto})";
                        db.Novedades.Add(novedad);
                    }

                    lote.CantidadAnimales = numero;
                    lote.PesoTotalGramos += cuyDto.PesoGramos;
                }

                // Condición de ayuno: aplica a la entrega de esta productora
                if (!dto.EnAyunas)
                {
                    db.Novedades.Add(new Novedad
                    {
                        LoteId = lote.Id,
                        Tipo = TipoNovedad.SinAyuno,
                        Descripcion = $"Entrega de {productora.NombreCompleto} recibida sin ayuno. " +
                                      "El peso registrado puede no ser el peso real.",
                        RegistradoPor = dto.ResponsableRecepcion
                    });
                }

                RecalcularEstadoLote(lote);

                if (lote.CantidadAnimales >= CapacidadJaula)
                {
                    lote.Cerrado = true;
                    lote.FechaCierre = DateTime.UtcNow;
                    seCompletoJaula = true;
                }

                await db.SaveChangesAsync();
            }

            // La marca de idempotencia se confirma junto con los datos:
            // o se guarda todo, o no se guarda nada
            if (dto.IdCliente is not null && dto.DispositivoId is not null)
            {
                db.SyncEntregasProcesadas.Add(new SyncEntregaProcesada
                {
                    DispositivoId = dto.DispositivoId,
                    IdCliente = dto.IdCliente
                });
                await db.SaveChangesAsync();
            }

            await transaccion.CommitAsync();

            return (lotesAfectados.Select(l => l.Id).ToList(), seCompletoJaula);
        });

        var respuesta = new List<LoteResponseDto>();
        foreach (var loteId in idsAfectados)
            respuesta.Add(await MapearLoteAsync(loteId));

        return new EntregaResultadoDto(
            CuyesRegistrados: dto.Cuyes.Count,
            LotesAfectados: respuesta,
            SeCompletoJaula: seCompleto
        );
    }

    public async Task<LoteResponseDto?> ObtenerLoteAbiertoAsync(CentroAcopio cat)
    {
        var lote = await db.Lotes
            .Where(l => l.CentroAcopio == cat && !l.Cerrado)
            .OrderBy(l => l.Id)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        return lote is null ? null : await MapearLoteAsync(lote.Id);
    }

    public async Task<LoteResponseDto?> CerrarLoteAsync(string codigoLote)
    {
        var lote = await db.Lotes
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote);

        if (lote is null) return null;

        if (lote.Cerrado)
            throw new InvalidOperationException(
                $"El lote {codigoLote} ya está cerrado.");

        if (lote.CantidadAnimales == 0)
            throw new InvalidOperationException(
                "No se puede cerrar una jaula vacía.");

        lote.Cerrado = true;
        lote.FechaCierre = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return await MapearLoteAsync(lote.Id);
    }

    private async Task<Lote> ObtenerOCrearJaulaAbiertaAsync(
        RegistrarEntregaDto dto, DateTime fechaUtc)
    {
        var abierta = await db.Lotes
            .Include(l => l.Cuyes)
            .Where(l => l.CentroAcopio == dto.CentroAcopio && !l.Cerrado)
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync();

        if (abierta is not null) return abierta;

        var nueva = new Lote
        {
            CodigoLote = await GenerarCodigoLoteAsync(dto.CentroAcopio, fechaUtc),
            // La primera productora que entrega queda como referencia histórica
            ProductoraId = dto.ProductoraId,
            CentroAcopio = dto.CentroAcopio,
            FechaRecepcion = fechaUtc,
            CantidadAnimales = 0,
            PesoTotalGramos = 0,
            Estado = EstadoLote.Aceptado,
            Cerrado = false,
            ResponsableRecepcion = dto.ResponsableRecepcion,
            Observaciones = dto.Observaciones,
            SincronizadoOffline = dto.SincronizadoOffline,
            FechaSincronizacion = dto.SincronizadoOffline ? DateTime.UtcNow : null
        };

        db.Lotes.Add(nueva);
        await db.SaveChangesAsync();
        return nueva;
    }

    private static void RecalcularEstadoLote(Lote lote)
    {
        if (lote.Cuyes.Count == 0) return;

        lote.Estado = lote.Cuyes.All(c => c.Estado == EstadoLote.Rechazado)
            ? EstadoLote.Rechazado
            : lote.Cuyes.Any(c => c.Estado != EstadoLote.Aceptado)
                ? EstadoLote.ConNovedad
                : EstadoLote.Aceptado;
    }

    // ── Consultas ─────────────────────────────────────────────────────

    public async Task<LoteResponseDto?> ObtenerLotePorIdAsync(int id)
    {
        var existe = await db.Lotes.AnyAsync(l => l.Id == id);
        return existe ? await MapearLoteAsync(id) : null;
    }

    public async Task<LoteResponseDto?> ObtenerLotePorCodigoAsync(string codigo)
    {
        var lote = await db.Lotes
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.CodigoLote == codigo);
        return lote is null ? null : await MapearLoteAsync(lote.Id);
    }

    public async Task<IEnumerable<LoteResponseDto>> ListarLotesAsync(
        CentroAcopio? cat, EstadoLote? estado, DateTime? desde, DateTime? hasta)
    {
        var query = db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Include(l => l.Movilizacion)
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
            .Take(MaxLotesListado)
            .AsNoTracking()
            // Con cinco colecciones incluidas, una sola consulta JOIN
            // multiplica filas de forma cartesiana; separarlas mantiene
            // el tamaño proporcional a los datos reales
            .AsSplitQuery()
            .ToListAsync();

        return lotes.Select(MapearLote);
    }

    // ── Sincronización offline — RF-211 ───────────────────────────────
    // Las entregas capturadas sin conexión se aplican en orden con la
    // misma lógica de acumulación en jaulas que un registro en línea.
    // Cada entrega produce UN resultado identificado por su IdCliente:
    // el dispositivo empareja por ese Id (nunca por posición) y los
    // reintentos de entregas ya procesadas no duplican animales.
    public async Task<SyncResultadoDto> SincronizarEntregasAsync(SyncEntregasDto dto)
    {
        var resultados = new List<SyncItemResultadoDto>();

        foreach (var entrega in dto.Entregas)
        {
            entrega.SincronizadoOffline = true;
            entrega.DispositivoId = dto.DispositivoId;

            try
            {
                await RegistrarEntregaAsync(entrega);
                resultados.Add(new SyncItemResultadoDto(
                    entrega.IdCliente, Exito: true, Duplicada: false, Motivo: null));
            }
            catch (EntregaDuplicadaException)
            {
                // Reintento de una entrega ya sincronizada: cuenta como
                // éxito para que el dispositivo la marque y deje de reenviarla
                resultados.Add(new SyncItemResultadoDto(
                    entrega.IdCliente, Exito: true, Duplicada: true, Motivo: null));
            }
            catch (Exception ex)
            {
                resultados.Add(new SyncItemResultadoDto(
                    entrega.IdCliente, Exito: false, Duplicada: false,
                    Motivo: ex.Message));
            }
        }

        return new SyncResultadoDto(
            TotalRecibidos: dto.Entregas.Count,
            TotalGuardados: resultados.Count(r => r.Exito && !r.Duplicada),
            TotalDuplicados: resultados.Count(r => r.Duplicada),
            TotalConError: resultados.Count(r => !r.Exito),
            Resultados: resultados
        );
    }

    // ── Evaluación individual por cuy — SRS Apéndice 5.1 ─────────────

    private static (CuyRegistro cuy, List<Novedad> novedades) EvaluarCuyIndividual(
        CuyRegistroDto c, int numero, string responsable)
    {
        var novedades = new List<Novedad>();
        var motivos = new List<string>();
        var rechazado = false;

        if (c.PesoGramos < 850)
        {
            rechazado = true;
            motivos.Add($"peso {c.PesoGramos:F0}g bajo el mínimo (850g)");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.BajoPeso,
                $"Peso {c.PesoGramos:F0}g por debajo del mínimo (850g). Animal rechazado.",
                responsable, c.PesoGramos));
        }
        else if (c.PesoGramos < 875)
        {
            motivos.Add($"peso justo ({c.PesoGramos:F0}g)");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.BajoPeso,
                $"Peso {c.PesoGramos:F0}g entre 850g–874g. Pasa con observación.",
                responsable, c.PesoGramos));
        }
        else if (c.PesoGramos > 1300)
        {
            motivos.Add($"sobre el rango operativo ({c.PesoGramos:F0}g)");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.SobrePeso,
                $"Peso {c.PesoGramos:F0}g sobre el rango operativo (máx. 1300g).",
                responsable, c.PesoGramos));
        }

        if (c.ColorPelaje.Equals("Negro", StringComparison.OrdinalIgnoreCase))
        {
            motivos.Add("piel negra");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.ColorNoConforme,
                "Piel completamente negra. No conforme para mercado formal.",
                responsable, null));
        }

        if (c.EstadoOreja.Equals("Dura", StringComparison.OrdinalIgnoreCase))
        {
            motivos.Add("oreja dura");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.OrejaDura,
                "Oreja dura: animal de edad avanzada.",
                responsable, null));
        }

        if (!string.IsNullOrWhiteSpace(c.SignosClinicos))
        {
            motivos.Add($"signos clínicos: {c.SignosClinicos.Trim()}");
            novedades.Add(NovedadDeCuy(numero, TipoNovedad.SignosClinicos,
                $"Condición sanitaria con observación: {c.SignosClinicos.Trim()}",
                responsable, null));
        }

        var cuy = new CuyRegistro
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
            MotivoNovedad = motivos.Count > 0 ? string.Join("; ", motivos) : null
        };

        return (cuy, novedades);
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

    // ── Generación de código de lote — SRS RF-103 / Apéndice 5.2 ─────
    // Formato: CAT-AAAAMMDD-SEC  ej: PAT-20260615-001

    private async Task<string> GenerarCodigoLoteAsync(
    CentroAcopio cat, DateTime fecha)
    {
        var prefijo = cat.ToString();
        var fechaUtc = DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
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
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .Include(l => l.Faenamientos).ThenInclude(f => f.Cuyes)
            .Include(l => l.Movilizacion)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstAsync(l => l.Id == loteId);

        return MapearLote(lote);
    }

    // Animales del lote aún no procesados en la planta
    internal static int CalcularDisponibles(Lote lote)
    {
        var usados = lote.Faenamientos.Sum(f =>
            f.Cuyes.Count > 0
                ? f.Cuyes.Count
                : f.UnidadesFaenadas + f.UnidadesDecomisadas);
        return Math.Max(0, lote.CantidadAnimales - usados);
    }

    private static LoteResponseDto MapearLote(Lote lote)
    {
        // Resumen de productoras que integran la jaula. Se agrupa por Id
        // y NUNCA por la instancia: con consultas sin tracking cada fila
        // materializa su propio objeto Productora, y agrupar por
        // referencia parte a una misma productora en N grupos de 1
        var productoras = lote.Cuyes
            .Where(c => c.Productora is not null)
            .GroupBy(c => c.ProductoraId)
            .Select(g =>
            {
                var p = g.First().Productora!;
                return new ProductoraEnLoteDto(
                    p.Id, p.NombreCompleto, p.Comunidad, g.Count());
            })
            .OrderByDescending(p => p.Cantidad)
            .ToList();

        if (productoras.Count == 0 && lote.Productora is not null)
        {
            productoras.Add(new ProductoraEnLoteDto(
                lote.Productora.Id, lote.Productora.NombreCompleto,
                lote.Productora.Comunidad, lote.CantidadAnimales));
        }

        var nombreProductora = productoras.Count switch
        {
            0 => string.Empty,
            1 => productoras[0].Nombre,
            _ => $"Varias productoras ({productoras.Count})"
        };

        return new LoteResponseDto(
            Id: lote.Id,
            CodigoLote: lote.CodigoLote,
            ProductoraId: lote.ProductoraId,
            NombreProductora: nombreProductora,
            CentroAcopio: lote.CentroAcopio.ToString(),
            FechaRecepcion: lote.FechaRecepcion,
            CantidadAnimales: lote.CantidadAnimales,
            PesoTotalGramos: lote.PesoTotalGramos,
            Estado: lote.Estado.ToString(),
            ResponsableRecepcion: lote.ResponsableRecepcion,
            Observaciones: lote.Observaciones,
            SincronizadoOffline: lote.SincronizadoOffline,
            Cerrado: lote.Cerrado,
            Disponibles: CalcularDisponibles(lote),
            TieneMovilizacion: lote.Movilizacion is not null,
            Productoras: productoras,
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
                    c.Estado.ToString(), c.MotivoNovedad,
                    c.Productora?.NombreCompleto))
                .ToList()
        );
    }
}
