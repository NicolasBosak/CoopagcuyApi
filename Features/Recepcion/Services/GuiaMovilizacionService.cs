using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CoopagcuyApi.Features.Recepcion.Services;

public interface IGuiaMovilizacionService
{
    Task<byte[]> GenerarGuiaPdfAsync(string codigoLote);
}

/// <summary>
/// Genera la guía/libretín digital de movilización de un lote — RF-210.
/// Documento imprimible que acompaña el transporte del lote desde el CAT
/// hasta la planta de faenamiento de Sulupali Chico.
/// </summary>
public class GuiaMovilizacionService(AppDbContext db) : IGuiaMovilizacionService
{
    private const string DestinoPlanta =
        "Planta de Faenamiento — Sulupali Chico, Santa Isabel, Azuay";

    public async Task<byte[]> GenerarGuiaPdfAsync(string codigoLote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var lote = await db.Lotes
            .Include(l => l.Productora)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

        // Productoras que integran la jaula, con su aporte de animales
        var contribuyentes = lote.Cuyes
            .Where(c => c.Productora is not null)
            .GroupBy(c => c.Productora!)
            .Select(g => (Productora: g.Key, Cantidad: g.Count()))
            .OrderByDescending(x => x.Cantidad)
            .ToList();

        if (contribuyentes.Count == 0 && lote.Productora is not null)
            contribuyentes.Add((lote.Productora, lote.CantidadAnimales));

        // Datos del transporte, si ya se registró la movilización
        var movilizacion = await db.Movilizaciones
            .FirstOrDefaultAsync(m => m.LoteId == lote.Id);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("GUÍA DE MOVILIZACIÓN")
                                .FontSize(15).Bold().FontColor("#2E7D32");
                            c.Item().Text("COOPAGCUY — Cuy Azuayito")
                                .FontSize(10).FontColor("#555555");
                        });
                        row.ConstantItem(110).AlignRight().Column(c =>
                        {
                            c.Item().Text(lote.CodigoLote)
                                .FontSize(13).Bold().FontColor("#B71C1C");
                            c.Item().Text($"Emitida: {DateTime.Now:dd/MM/yyyy HH:mm}")
                                .FontSize(7).FontColor("#777777");
                        });
                    });
                    col.Item().PaddingTop(4).BorderBottom(2).BorderColor("#2E7D32");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Background("#F1F8E9").Padding(10).Column(c =>
                    {
                        c.Item().Text("ORIGEN — PRODUCTORAS DEL LOTE")
                            .FontSize(8).Bold().FontColor("#2E7D32");
                        c.Item().PaddingTop(3).Text(
                            $"Centro de acopio: {lote.CentroAcopio}");

                        foreach (var (productora, cantidad) in contribuyentes)
                        {
                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem(3).Text(
                                    $"• {productora.NombreCompleto} " +
                                    $"({productora.Comunidad}, {productora.Canton})");
                                r.RelativeItem(1).AlignRight().Text(
                                    $"{cantidad} {(cantidad == 1 ? "cuy" : "cuyes")}")
                                    .Bold();
                            });
                        }
                    });

                    col.Item().PaddingTop(8).Background("#E3F2FD").Padding(10).Column(c =>
                    {
                        c.Item().Text("DESTINO").FontSize(8).Bold().FontColor("#1565C0");
                        c.Item().PaddingTop(3).Text(DestinoPlanta);
                    });

                    col.Item().PaddingTop(8).Background("#FAFAFA").Padding(10).Column(c =>
                    {
                        c.Item().Text("DETALLE DEL LOTE").FontSize(8).Bold().FontColor("#444444");
                        c.Item().PaddingTop(3).Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Cantidad: {lote.CantidadAnimales} animales");
                            r.RelativeItem().Text(
                                $"Peso total: {lote.PesoTotalGramos:N0} g");
                        });
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Text(
                                $"Recepción: {lote.FechaRecepcion:dd/MM/yyyy HH:mm}");
                            r.RelativeItem().Text(
                                $"Estado: {lote.Estado}");
                        });
                        c.Item().Text(
                            $"Responsable de recepción: {lote.ResponsableRecepcion ?? "-"}");

                        if (lote.Novedades.Count > 0)
                        {
                            c.Item().PaddingTop(4).Text("Novedades:")
                                .FontSize(8).Bold().FontColor("#E65100");
                            foreach (var n in lote.Novedades)
                                c.Item().Text($"• {n.Tipo}: {n.Descripcion}").FontSize(8);
                        }
                    });

                    // Detalle individual: los animales se registraron uno
                    // por uno y la guía refleja ese nivel de detalle
                    if (lote.Cuyes.Count > 0)
                    {
                        col.Item().PaddingTop(8).Background("#FAFAFA").Padding(10).Column(c =>
                        {
                            c.Item().Text("DETALLE POR ANIMAL")
                                .FontSize(8).Bold().FontColor("#444444");

                            c.Item().PaddingTop(4).Table(tabla =>
                            {
                                tabla.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(25);   // N°
                                    cols.RelativeColumn(3);    // Productora
                                    cols.ConstantColumn(55);   // Peso
                                    cols.RelativeColumn(2);    // Características
                                    cols.ConstantColumn(65);   // Estado
                                });

                                tabla.Header(h =>
                                {
                                    foreach (var titulo in new[]
                                        { "N°", "Productora", "Peso",
                                          "Características", "Estado" })
                                    {
                                        h.Cell().BorderBottom(1).BorderColor("#CCCCCC")
                                            .PaddingBottom(2)
                                            .Text(titulo).FontSize(7).Bold();
                                    }
                                });

                                foreach (var cuy in lote.Cuyes
                                    .OrderBy(x => x.NumeroEnLote))
                                {
                                    var colorEstado = cuy.Estado switch
                                    {
                                        Common.EstadoLote.Rechazado => "#B71C1C",
                                        Common.EstadoLote.ConNovedad => "#E65100",
                                        _ => "#2E7D32"
                                    };

                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{cuy.NumeroEnLote}").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(cuy.Productora is not null
                                            ? $"{cuy.Productora.NombreCompleto} ({cuy.Productora.Comunidad})"
                                            : "—").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{cuy.PesoGramos:F0}g").FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{cuy.ColorPelaje} · {cuy.EstadoOreja} · {cuy.TamanoAnimal}")
                                        .FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(cuy.Estado.ToString()).FontSize(7)
                                        .FontColor(colorEstado);
                                }
                            });
                        });
                    }

                    // Datos del transporte y declaración de tratamientos
                    if (movilizacion is not null)
                    {
                        col.Item().PaddingTop(8).Background("#FFF3E0").Padding(10).Column(c =>
                        {
                            c.Item().Text("TRANSPORTE").FontSize(8).Bold().FontColor("#E65100");
                            c.Item().PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Conductor: {movilizacion.Conductor}");
                                r.RelativeItem().Text(
                                    $"Despacho: {movilizacion.FechaDespacho:dd/MM/yyyy HH:mm}");
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Cantidad movilizada: {movilizacion.CantidadMovilizada}");
                                r.RelativeItem().Text(
                                    $"Condiciones: {movilizacion.CondicionesTransporte ?? "-"}");
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Forraje: {movilizacion.TipoForraje ?? "-"}");
                                r.RelativeItem().Text(
                                    "Retiro de medicamentos: " +
                                    (movilizacion.DiasRetiroMedicamentos is int dias
                                        ? $"{dias} días" : "sin declaración"));
                            });
                            if (movilizacion.FechaRecepcionPlanta is not null)
                            {
                                c.Item().Text(
                                    $"Recibido en planta: {movilizacion.FechaRecepcionPlanta:dd/MM/yyyy HH:mm} " +
                                    $"por {movilizacion.RecibidoPor}");
                            }
                        });
                    }

                    // Firmas de entrega y recepción
                    col.Item().PaddingTop(28).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().BorderTop(1).BorderColor("#999999")
                                .PaddingTop(3).AlignCenter()
                                .Text("Entrega (Operador CAT)").FontSize(8);
                        });
                        r.ConstantItem(30);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().BorderTop(1).BorderColor("#999999")
                                .PaddingTop(3).AlignCenter()
                                .Text("Recibe (Transportista / Planta)").FontSize(8);
                        });
                    });
                });

                page.Footer().AlignCenter().Text(
                    "Documento de respaldo interno. No reemplaza guías sanitarias oficiales (AGROCALIDAD).")
                    .FontSize(7).FontColor("#999999");
            });
        }).GeneratePdf();
    }
}
