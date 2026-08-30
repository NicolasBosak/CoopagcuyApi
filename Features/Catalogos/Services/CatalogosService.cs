using System.Text.RegularExpressions;
using CoopagcuyApi.Features.Catalogos.DTOs;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Catalogos.Services;

public interface ICatalogosService
{
    Task<IEnumerable<ComunidadResponseDto>> ListarComunidadesAsync(bool incluirInactivas);
    Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto);
    Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto);
    Task<bool> CambiarEstadoComunidadAsync(int id, bool activa);

    Task<IEnumerable<CentroAcopioDto>> ListarCentrosAcopioAsync(bool incluirInactivos);
    Task<CentroAcopioDto> CrearCentroAcopioAsync(CrearCentroAcopioDto dto);
    Task<bool> ActualizarCentroAcopioAsync(string codigo, ActualizarCentroAcopioDto dto);
    Task<bool> CambiarEstadoCentroAcopioAsync(string codigo, bool activo);
}

public partial class CatalogosService(AppDbContext db) : ICatalogosService
{
    // Tres letras A–Z, ni una más. El código prefija el identificador de cada
    // jaula (PAT-20260615-001): con ancho variable se romperían las etiquetas
    // ink-jet y cualquier lectura por posición.
    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodigoCat();

    // ── Comunidades ──────────────────────────────────────────────────

    public async Task<IEnumerable<ComunidadResponseDto>> ListarComunidadesAsync(
        bool incluirInactivas)
    {
        var query = db.Comunidades.AsQueryable();
        if (!incluirInactivas) query = query.Where(c => c.Activa);

        return await query
            .OrderBy(c => c.Nombre)
            .Select(c => new ComunidadResponseDto(
                c.Id, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.CatReferencia, c.Activa,
                c.Latitud, c.Longitud, c.AltitudMinM, c.AltitudMaxM))
            .ToListAsync();
    }

    public async Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto)
    {
        var nombre = dto.Nombre.Trim();
        var cat = dto.CatReferencia.Trim().ToUpperInvariant();

        await ValidarComunidadAsync(nombre, dto.CantonId, cat, idExcluido: null);

        var comunidad = new Comunidad
        {
            Nombre = nombre,
            CantonId = dto.CantonId,
            CatReferencia = cat,
            Latitud = dto.Latitud,
            Longitud = dto.Longitud,
            AltitudMinM = dto.AltitudMinM,
            AltitudMaxM = dto.AltitudMaxM,
        };

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();

        return await LeerComunidadAsync(comunidad.Id);
    }

    public async Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto)
    {
        var comunidad = await db.Comunidades.FindAsync(id);
        if (comunidad is null) return false;

        var nombre = dto.Nombre.Trim();
        var cat = dto.CatReferencia.Trim().ToUpperInvariant();

        await ValidarComunidadAsync(nombre, dto.CantonId, cat, idExcluido: id);

        comunidad.Nombre = nombre;
        comunidad.CantonId = dto.CantonId;
        comunidad.CatReferencia = cat;
        comunidad.Latitud = dto.Latitud;
        comunidad.Longitud = dto.Longitud;
        comunidad.AltitudMinM = dto.AltitudMinM;
        comunidad.AltitudMaxM = dto.AltitudMaxM;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoComunidadAsync(int id, bool activa)
    {
        var comunidad = await db.Comunidades.FindAsync(id);
        if (comunidad is null) return false;

        comunidad.Activa = activa;
        await db.SaveChangesAsync();
        return true;
    }

    // El nombre es único DENTRO del cantón, no en todo el sistema: "San José"
    // existe en varias provincias del Ecuador.
    //
    // NO se valida que el CAT sea del mismo cantón ni de la misma provincia:
    // una comunidad entrega donde le queda más cerca, y hay comunidades a las
    // que les queda más cerca un centro de la provincia de al lado.
    private async Task ValidarComunidadAsync(
        string nombre, int cantonId, string cat, int? idExcluido)
    {
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la comunidad es obligatorio.");

        if (!await db.Cantones.AnyAsync(c => c.Id == cantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        if (!await db.CentrosAcopio.AnyAsync(c => c.Codigo == cat && c.Activo))
            throw new InvalidOperationException(
                $"El centro de acopio '{cat}' no existe o está inactivo.");

        var repetida = await db.Comunidades.AnyAsync(c =>
            c.CantonId == cantonId
            && c.Nombre.ToLower() == nombre.ToLower()
            && (idExcluido == null || c.Id != idExcluido));

        if (repetida)
            throw new InvalidOperationException(
                $"Ya existe la comunidad '{nombre}' en ese cantón.");
    }

    private Task<ComunidadResponseDto> LeerComunidadAsync(int id) =>
        db.Comunidades
            .Where(c => c.Id == id)
            .Select(c => new ComunidadResponseDto(
                c.Id, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.CatReferencia, c.Activa,
                c.Latitud, c.Longitud, c.AltitudMinM, c.AltitudMaxM))
            .SingleAsync();

    // ── Centros de acopio ────────────────────────────────────────────

    public async Task<IEnumerable<CentroAcopioDto>> ListarCentrosAcopioAsync(
        bool incluirInactivos)
    {
        var query = db.CentrosAcopio.AsQueryable();
        if (!incluirInactivos) query = query.Where(c => c.Activo);

        return await query
            .OrderBy(c => c.Nombre)
            .Select(c => new CentroAcopioDto(
                c.Codigo, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.Activo))
            .ToListAsync();
    }

    public async Task<CentroAcopioDto> CrearCentroAcopioAsync(CrearCentroAcopioDto dto)
    {
        var codigo = dto.Codigo.Trim().ToUpperInvariant();
        var nombre = dto.Nombre.Trim();

        if (!CodigoCat().IsMatch(codigo))
            throw new InvalidOperationException(
                "El código del centro debe ser exactamente tres letras (por ejemplo, PAT).");

        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del centro es obligatorio.");

        if (await db.CentrosAcopio.AnyAsync(c => c.Codigo == codigo))
            throw new InvalidOperationException(
                $"Ya existe un centro de acopio con el código '{codigo}'.");

        if (!await db.Cantones.AnyAsync(c => c.Id == dto.CantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        db.CentrosAcopio.Add(new CentroAcopio
        {
            Codigo = codigo,
            Nombre = nombre,
            CantonId = dto.CantonId,
        });
        await db.SaveChangesAsync();

        return await LeerCentroAsync(codigo);
    }

    // El código no se toca: no está en el DTO y aquí tampoco se lee.
    public async Task<bool> ActualizarCentroAcopioAsync(
        string codigo, ActualizarCentroAcopioDto dto)
    {
        var centro = await db.CentrosAcopio.FindAsync(codigo.Trim().ToUpperInvariant());
        if (centro is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del centro es obligatorio.");

        if (!await db.Cantones.AnyAsync(c => c.Id == dto.CantonId && c.Activo))
            throw new InvalidOperationException("El cantón indicado no existe o está inactivo.");

        centro.Nombre = nombre;
        centro.CantonId = dto.CantonId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoCentroAcopioAsync(string codigo, bool activo)
    {
        var clave = codigo.Trim().ToUpperInvariant();
        var centro = await db.CentrosAcopio.FindAsync(clave);
        if (centro is null) return false;

        if (!activo)
        {
            // Una jaula abierta son cuyes esperando físicamente en ese centro.
            var jaulasAbiertas = await db.Lotes
                .CountAsync(l => l.CentroAcopio == clave && !l.Cerrado);

            if (jaulasAbiertas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{centro.Nombre}': tiene {jaulasAbiertas} " +
                    "jaula(s) abierta(s). Ciérralas primero.");

            var productorasVivas = await db.Productoras
                .CountAsync(p => p.CatAsignado == clave && p.Activa);

            if (productorasVivas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{centro.Nombre}': todavía entregan " +
                    $"{productorasVivas} productora(s) activa(s). Reasígnalas primero.");
        }

        centro.Activo = activo;
        await db.SaveChangesAsync();
        return true;
    }

    private Task<CentroAcopioDto> LeerCentroAsync(string codigo) =>
        db.CentrosAcopio
            .Where(c => c.Codigo == codigo)
            .Select(c => new CentroAcopioDto(
                c.Codigo, c.Nombre, c.CantonId, c.Canton.Nombre,
                c.Canton.Provincia.Nombre, c.Activo))
            .SingleAsync();
}
