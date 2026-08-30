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
                c.Id, c.Nombre, c.Canton.Nombre, c.CatReferencia.ToString(), c.Activa))
            .ToListAsync();
    }

    // Sin "async": el cuerpo solo lanza, no hay ningún "await" que justifique
    // el modificador (y dejarlo produciría CS1998). El throw es síncrono,
    // pero como el llamador siempre lo hace dentro de un "await service...()"
    // envuelto en try/catch, el catch lo atrapa igual.
    public Task<ComunidadResponseDto> CrearComunidadAsync(GuardarComunidadDto dto)
    {
        // El alta contra el catálogo de cantones llega en la Task 3. Hasta
        // entonces esto falla explícito y no con una violación de clave
        // foránea que el controlador no sabe traducir: un 409 con causa se
        // lee, un 500 sin cuerpo no.
        throw new InvalidOperationException(
            "El alta de comunidades está temporalmente deshabilitada: " +
            "requiere el catálogo de cantones.");
    }

    public Task<bool> ActualizarComunidadAsync(int id, GuardarComunidadDto dto)
    {
        // La edición contra el catálogo de cantones llega en la Task 3. Hasta
        // entonces esto falla explícito: sin este throw, dto.Canton se
        // descartaba en silencio y la API respondía 204 sin haber cambiado
        // el cantón que el administrador pidió cambiar.
        throw new InvalidOperationException(
            "La edición de comunidades está temporalmente deshabilitada: " +
            "requiere el catálogo de cantones.");
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
