using CoopagcuyApi.Common;
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
    IEnumerable<CentroAcopioDto> ListarCentrosAcopio();
}

public class CatalogosService(AppDbContext db) : ICatalogosService
{
    private static readonly Dictionary<CentroAcopio, string> NombresCat = new()
    {
        [CentroAcopio.PAT] = "Patococha",
        [CentroAcopio.NIE] = "Las Nieves",
        [CentroAcopio.HUE] = "Huertas",
        [CentroAcopio.NAB] = "Nabón / El Progreso",
        [CentroAcopio.PEL] = "Pelincay"
    };

    public async Task<IEnumerable<ComunidadResponseDto>> ListarComunidadesAsync(
        bool incluirInactivas)
    {
        var query = db.Comunidades.AsQueryable();
        if (!incluirInactivas)
            query = query.Where(c => c.Activa);

        return await query
            .OrderBy(c => c.Nombre)
            .Select(c => new ComunidadResponseDto(
                c.Id, c.Nombre, c.Canton, c.CatReferencia.ToString(), c.Activa))
            .ToListAsync();
    }

    public async Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto)
    {
        var nombre = dto.Nombre.Trim();
        var existe = await db.Comunidades
            .AnyAsync(c => c.Nombre.ToLower() == nombre.ToLower());

        if (existe)
            throw new InvalidOperationException(
                $"Ya existe una comunidad con el nombre '{nombre}'.");

        var comunidad = new Comunidad
        {
            Nombre = nombre,
            Canton = dto.Canton.Trim(),
            CatReferencia = dto.CatReferencia
        };

        db.Comunidades.Add(comunidad);
        await db.SaveChangesAsync();

        return new ComunidadResponseDto(
            comunidad.Id, comunidad.Nombre, comunidad.Canton,
            comunidad.CatReferencia.ToString(), comunidad.Activa);
    }

    public async Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto)
    {
        var comunidad = await db.Comunidades.FindAsync(id);
        if (comunidad is null) return false;

        comunidad.Nombre = dto.Nombre.Trim();
        comunidad.Canton = dto.Canton.Trim();
        comunidad.CatReferencia = dto.CatReferencia;

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

    public IEnumerable<CentroAcopioDto> ListarCentrosAcopio() =>
        NombresCat.Select(kv => new CentroAcopioDto(kv.Key.ToString(), kv.Value));
}
