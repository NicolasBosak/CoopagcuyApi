using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Features.Productoras.Models;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Productoras.Services;

public interface IProductoraService
{
    Task<ProductoraResponseDto> CrearAsync(CrearProductoraDto dto);
    Task<IEnumerable<ProductoraResponseDto>> ObtenerTodasAsync(string? comunidad, string? cat);
    Task<ProductoraResponseDto?> ObtenerPorIdAsync(int id);
    Task<bool> ActualizarAsync(int id, CrearProductoraDto dto);
}

public class ProductoraService(AppDbContext db) : IProductoraService
{
    public async Task<ProductoraResponseDto> CrearAsync(CrearProductoraDto dto)
    {
        var productora = new Productora
        {
            NombreCompleto = dto.NombreCompleto,
            Cedula = dto.Cedula,
            Comunidad = dto.Comunidad,
            Canton = dto.Canton,
            CatAsignado = dto.CatAsignado,
            Telefono = dto.Telefono
        };

        db.Productoras.Add(productora);
        await db.SaveChangesAsync();
        return MapToDto(productora);
    }

    public async Task<IEnumerable<ProductoraResponseDto>> ObtenerTodasAsync(
        string? comunidad, string? cat)
    {
        var query = db.Productoras.Where(p => p.Activa);

        if (!string.IsNullOrEmpty(comunidad))
            query = query.Where(p => p.Comunidad.Contains(comunidad));

        if (!string.IsNullOrEmpty(cat) && Enum.TryParse<Common.CentroAcopio>(cat, out var catEnum))
            query = query.Where(p => p.CatAsignado == catEnum);

        return await query.Select(p => MapToDto(p)).ToListAsync();
    }

    public async Task<ProductoraResponseDto?> ObtenerPorIdAsync(int id)
    {
        var p = await db.Productoras.FindAsync(id);
        return p is null ? null : MapToDto(p);
    }

    public async Task<bool> ActualizarAsync(int id, CrearProductoraDto dto)
    {
        var productora = await db.Productoras.FindAsync(id);
        if (productora is null) return false;

        productora.NombreCompleto = dto.NombreCompleto;
        productora.Comunidad = dto.Comunidad;
        productora.Canton = dto.Canton;
        productora.CatAsignado = dto.CatAsignado;
        productora.Telefono = dto.Telefono;

        await db.SaveChangesAsync();
        return true;
    }

    private static ProductoraResponseDto MapToDto(Productora p) => new(
        p.Id, p.NombreCompleto, p.Cedula, p.Comunidad,
        p.Canton, p.CatAsignado.ToString(), p.Telefono,
        p.Activa, p.FechaRegistro
    );
}