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
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

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
                        c.Item().Text("ORIGEN").FontSize(8).Bold().FontColor("#2E7D32");
                        c.Item().PaddingTop(3).Text(
                            $"Productora: {lote.Productora.NombreCompleto}");
                        c.Item().Text(
                            $"Comunidad: {lote.Productora.Comunidad} · Cantón: {lote.Productora.Canton}");
                        c.Item().Text(
                            $"Centro de acopio: {lote.CentroAcopio}");
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
