using CoopagcuyApi.Features.Catalogos.DTOs;
using CoopagcuyApi.Features.Catalogos.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Catalogos.Services;

public interface IGeografiaService
{
    Task<IEnumerable<ProvinciaDto>> ListarProvinciasAsync(bool incluirInactivas);
    Task<ProvinciaDto> CrearProvinciaAsync(GuardarProvinciaDto dto);
    Task<bool> ActualizarProvinciaAsync(int id, GuardarProvinciaDto dto);
    Task<bool> CambiarEstadoProvinciaAsync(int id, bool activa);

    Task<IEnumerable<CantonDto>> ListarCantonesAsync(int? provinciaId, bool incluirInactivos);
    Task<CantonDto> CrearCantonAsync(GuardarCantonDto dto);
    Task<bool> ActualizarCantonAsync(int id, GuardarCantonDto dto);
    Task<bool> CambiarEstadoCantonAsync(int id, bool activo);
}

/// <summary>
/// Alta y baja del catálogo geográfico. Nada se borra: se desactiva, y no se
/// desactiva lo que todavía sostiene a otros. Un cantón dado de baja con
/// comunidades vivas dejaría fichas públicas sin poder decir de dónde es el cuy.
/// </summary>
public class GeografiaService(AppDbContext db) : IGeografiaService
{
    // ── Provincias ───────────────────────────────────────────────────

    public async Task<IEnumerable<ProvinciaDto>> ListarProvinciasAsync(bool incluirInactivas)
    {
        var query = db.Provincias.AsQueryable();
        if (!incluirInactivas) query = query.Where(p => p.Activa);

        return await query
            .OrderBy(p => p.Nombre)
            .Select(p => new ProvinciaDto(
                p.Id, p.Nombre, p.Activa,
                p.Cantones.Count(c => c.Activo)))
            .ToListAsync();
    }

    public async Task<ProvinciaDto> CrearProvinciaAsync(GuardarProvinciaDto dto)
    {
        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la provincia es obligatorio.");

        if (await db.Provincias.AnyAsync(p => p.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException($"Ya existe la provincia '{nombre}'.");

        var provincia = new Provincia { Nombre = nombre };
        db.Provincias.Add(provincia);
        await db.SaveChangesAsync();

        return new ProvinciaDto(provincia.Id, provincia.Nombre, provincia.Activa, 0);
    }

    public async Task<bool> ActualizarProvinciaAsync(int id, GuardarProvinciaDto dto)
    {
        var provincia = await db.Provincias.FindAsync(id);
        if (provincia is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre de la provincia es obligatorio.");

        if (await db.Provincias.AnyAsync(p =>
                p.Id != id && p.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException($"Ya existe la provincia '{nombre}'.");

        provincia.Nombre = nombre;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoProvinciaAsync(int id, bool activa)
    {
        var provincia = await db.Provincias.FindAsync(id);
        if (provincia is null) return false;

        if (!activa)
        {
            var cantonesVivos = await db.Cantones
                .CountAsync(c => c.ProvinciaId == id && c.Activo);

            if (cantonesVivos > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{provincia.Nombre}': todavía tiene " +
                    $"{cantonesVivos} cantón(es) activo(s). Desactívalos primero.");
        }

        provincia.Activa = activa;
        await db.SaveChangesAsync();
        return true;
    }

    // ── Cantones ─────────────────────────────────────────────────────

    public async Task<IEnumerable<CantonDto>> ListarCantonesAsync(
        int? provinciaId, bool incluirInactivos)
    {
        var query = db.Cantones.AsQueryable();
        if (provinciaId is int id) query = query.Where(c => c.ProvinciaId == id);
        if (!incluirInactivos) query = query.Where(c => c.Activo);

        return await query
            .OrderBy(c => c.Provincia.Nombre).ThenBy(c => c.Nombre)
            .Select(c => new CantonDto(
                c.Id, c.Nombre, c.ProvinciaId, c.Provincia.Nombre, c.Activo,
                db.Comunidades.Count(x => x.CantonId == c.Id && x.Activa)))
            .ToListAsync();
    }

    public async Task<CantonDto> CrearCantonAsync(GuardarCantonDto dto)
    {
        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del cantón es obligatorio.");

        var provincia = await db.Provincias.FindAsync(dto.ProvinciaId)
            ?? throw new InvalidOperationException("La provincia indicada no existe.");

        // Único DENTRO de la provincia: hay cantones homónimos en el Ecuador
        // ("Bolívar" está en Carchi y en Manabí).
        if (await db.Cantones.AnyAsync(c =>
                c.ProvinciaId == dto.ProvinciaId && c.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException(
                $"Ya existe el cantón '{nombre}' en {provincia.Nombre}.");

        var canton = new Canton { Nombre = nombre, ProvinciaId = dto.ProvinciaId };
        db.Cantones.Add(canton);
        await db.SaveChangesAsync();

        return new CantonDto(canton.Id, canton.Nombre, provincia.Id, provincia.Nombre,
            canton.Activo, 0);
    }

    public async Task<bool> ActualizarCantonAsync(int id, GuardarCantonDto dto)
    {
        var canton = await db.Cantones.FindAsync(id);
        if (canton is null) return false;

        var nombre = dto.Nombre.Trim();
        if (nombre.Length == 0)
            throw new InvalidOperationException("El nombre del cantón es obligatorio.");

        if (!await db.Provincias.AnyAsync(p => p.Id == dto.ProvinciaId))
            throw new InvalidOperationException("La provincia indicada no existe.");

        if (await db.Cantones.AnyAsync(c =>
                c.Id != id && c.ProvinciaId == dto.ProvinciaId
                && c.Nombre.ToLower() == nombre.ToLower()))
            throw new InvalidOperationException(
                $"Ya existe otro cantón '{nombre}' en esa provincia.");

        canton.Nombre = nombre;
        canton.ProvinciaId = dto.ProvinciaId;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CambiarEstadoCantonAsync(int id, bool activo)
    {
        var canton = await db.Cantones.FindAsync(id);
        if (canton is null) return false;

        if (!activo)
        {
            var comunidadesVivas = await db.Comunidades
                .CountAsync(c => c.CantonId == id && c.Activa);

            if (comunidadesVivas > 0)
                throw new InvalidOperationException(
                    $"No se puede desactivar '{canton.Nombre}': todavía tiene " +
                    $"{comunidadesVivas} comunidad(es) activa(s). Desactívalas primero.");
        }

        canton.Activo = activo;
        await db.SaveChangesAsync();
        return true;
    }
}
