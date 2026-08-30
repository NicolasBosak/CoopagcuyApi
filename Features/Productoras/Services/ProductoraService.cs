using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Productoras.Services;

public interface IProductoraService
{
    Task<ProductoraResponseDto> CrearAsync(CrearProductoraDto dto);
    Task<IEnumerable<ProductoraResponseDto>> ObtenerTodasAsync(
        string? comunidad, string? cat, bool incluirInactivas = false);
    Task<ProductoraResponseDto?> ObtenerPorIdAsync(int id);
    Task<bool> ActualizarAsync(int id, CrearProductoraDto dto, string modificadoPor);
    Task<bool> CambiarEstadoAsync(int id, bool activa);
    Task<IEnumerable<ProductoraCambioDto>> ObtenerHistorialAsync(int id);
}

public class ProductoraService(AppDbContext db) : IProductoraService
{
    public async Task<ProductoraResponseDto> CrearAsync(CrearProductoraDto dto)
    {
        var cedula = dto.Cedula.Trim();
        dto.CatAsignado = NormalizarCat(dto.CatAsignado);

        // La cédula se valida aquí y no solo en el validador del controlador,
        // igual que en UsuarioService. El validador cubre el endpoint actual,
        // pero es una defensa de una sola capa: cualquier otra vía de entrada
        // —una importación, un endpoint nuevo, un seeder— llegaría al servicio
        // sin pasar por él. La regla vive donde se escribe el dato.
        if (!ValidadorCedula.EsValida(cedula))
            throw new InvalidOperationException(
                "El número de cédula ingresado no es válido.");

        var existe = await db.Productoras.AnyAsync(p => p.Cedula == cedula);
        if (existe)
            throw new InvalidOperationException(
                $"Ya existe una productora registrada con la cédula {cedula}.");

        // Con Include, no FindAsync: MapToDto necesita el cantón cargado y
        // FindAsync no admite Include.
        var comunidad = await db.Comunidades
            .Include(c => c.Canton)
            .FirstOrDefaultAsync(c => c.Id == dto.ComunidadId)
            ?? throw new InvalidOperationException(
                $"La comunidad con Id {dto.ComunidadId} no existe.");

        var productora = new Productora
        {
            NombreCompleto = dto.NombreCompleto,
            Cedula = cedula,
            ComunidadId = comunidad.Id,
            Comunidad = comunidad,
            CatAsignado = dto.CatAsignado,
            Telefono = dto.Telefono
        };

        db.Productoras.Add(productora);
        await db.SaveChangesAsync();
        return MapToDto(productora);
    }

    public async Task<IEnumerable<ProductoraResponseDto>> ObtenerTodasAsync(
        string? comunidad, string? cat, bool incluirInactivas = false)
    {
        // La administración pide incluir inactivas para poder reactivarlas;
        // el resto de pantallas solo ve las activas
        var query = incluirInactivas
            ? db.Productoras.AsQueryable()
            : db.Productoras.Where(p => p.Activa);

        if (!string.IsNullOrEmpty(comunidad))
            query = query.Where(p => p.Comunidad.Nombre.Contains(comunidad));

        if (!string.IsNullOrEmpty(cat))
            query = query.Where(p => p.CatAsignado == cat);

        // Incluye el conteo de cuyes retornados desde la planta,
        // para que el CAT sepa qué productora presenta devoluciones
        return await query
            .Select(p => new ProductoraResponseDto(
                p.Id, p.NombreCompleto, p.Cedula, p.ComunidadId,
                p.Comunidad.Nombre, p.Comunidad.Canton.Nombre,
                p.CatAsignado, p.Telefono,
                p.Activa, p.FechaRegistro,
                db.RetornosProductora.Count(r => r.ProductoraId == p.Id)))
            .ToListAsync();
    }

    public async Task<ProductoraResponseDto?> ObtenerPorIdAsync(int id)
    {
        var p = await db.Productoras
            .Include(p => p.Comunidad)
            .ThenInclude(c => c.Canton)
            .FirstOrDefaultAsync(p => p.Id == id);
        return p is null ? null : MapToDto(p);
    }

    // Actualiza la productora registrando cada campo modificado — RF-105
    public async Task<bool> ActualizarAsync(int id, CrearProductoraDto dto, string modificadoPor)
    {
        var productora = await db.Productoras
            .Include(p => p.Comunidad)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (productora is null) return false;

        var nuevaComunidad = await db.Comunidades.FindAsync(dto.ComunidadId)
            ?? throw new InvalidOperationException(
                $"La comunidad con Id {dto.ComunidadId} no existe.");

        // Antes de comparar contra lo guardado: si no, el historial
        // registraría un cambio de "PAT" a "pat" que no es tal.
        dto.CatAsignado = NormalizarCat(dto.CatAsignado);

        RegistrarCambio(id, "NombreCompleto", productora.NombreCompleto, dto.NombreCompleto, modificadoPor);
        // El historial guarda el nombre, no el Id: quien lo lee necesita
        // entenderlo sin resolver claves contra el catálogo
        RegistrarCambio(id, "Comunidad", productora.Comunidad.Nombre, nuevaComunidad.Nombre, modificadoPor);
        RegistrarCambio(id, "CatAsignado", productora.CatAsignado, dto.CatAsignado, modificadoPor);
        RegistrarCambio(id, "Telefono", productora.Telefono, dto.Telefono, modificadoPor);

        productora.NombreCompleto = dto.NombreCompleto;
        productora.ComunidadId = nuevaComunidad.Id;
        productora.Comunidad = nuevaComunidad;
        productora.CatAsignado = dto.CatAsignado;
        productora.Telefono = dto.Telefono;

        await db.SaveChangesAsync();
        return true;
    }

    // Baja/alta lógica: conserva todo el historial de la productora
    public async Task<bool> CambiarEstadoAsync(int id, bool activa)
    {
        var productora = await db.Productoras.FindAsync(id);
        if (productora is null) return false;

        productora.Activa = activa;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<ProductoraCambioDto>> ObtenerHistorialAsync(int id)
    {
        return await db.ProductoraCambios
            .Where(c => c.ProductoraId == id)
            .OrderByDescending(c => c.FechaCambio)
            .Select(c => new ProductoraCambioDto(
                c.Id, c.CampoModificado, c.ValorAnterior,
                c.ValorNuevo, c.ModificadoPor, c.FechaCambio))
            .ToListAsync();
    }

    private void RegistrarCambio(
        int productoraId, string campo,
        string? anterior, string? nuevo, string modificadoPor)
    {
        if (anterior == nuevo) return;

        db.ProductoraCambios.Add(new ProductoraCambio
        {
            ProductoraId = productoraId,
            CampoModificado = campo,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            ModificadoPor = modificadoPor
        });
    }

    // El código del CAT se normaliza AQUÍ, en el borde, y no en cada
    // consulta: Postgres distingue mayúsculas y una productora guardada
    // con "pat" dejaría al operador de PAT viendo una bandeja vacía sin
    // ningún error que lo explique.
    private static string NormalizarCat(string? cat) =>
        (cat ?? string.Empty).Trim().ToUpperInvariant();

    // Requiere Comunidad y Comunidad.Canton cargados (Include/ThenInclude o
    // asignados al crear)
    private static ProductoraResponseDto MapToDto(Productora p) => new(
        p.Id, p.NombreCompleto, p.Cedula, p.ComunidadId,
        p.Comunidad.Nombre, p.Comunidad.Canton.Nombre,
        p.CatAsignado, p.Telefono,
        p.Activa, p.FechaRegistro
    );
}
