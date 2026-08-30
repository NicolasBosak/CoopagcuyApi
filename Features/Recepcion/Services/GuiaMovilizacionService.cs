using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Branding;
using CoopagcuyApi.Features.Recepcion.Models;
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

    /// <summary>
    /// Línea sanitaria de la guía. Es público y estático para poder fijarlo
    /// por unidad: el PDF comprime su texto y no hay forma razonable de
    /// afirmar nada sobre el binario.
    /// </summary>
    public static string TextoDeclaracionSanitaria(Movilizacion movilizacion) =>
        movilizacion.SinAntibioticos7Dias == true
            ? "Sin antibióticos últimos 7 días: declarado por " +
              movilizacion.ResponsableDespacho
            // Movilización anterior al cambio: se conserva el dato que sí se
            // capturó entonces en vez de imprimir una línea vacía.
            : movilizacion.DiasRetiroMedicamentos is int dias
                ? $"Retiro de medicamentos: {dias} días"
                : "Declaración sanitaria: sin declaración";

    public async Task<byte[]> GenerarGuiaPdfAsync(string codigoLote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var lote = await db.Lotes
            // El ThenInclude de Comunidad es explícito a propósito. Funciona
            // igual sin él, porque Productora.Comunidad está marcada como
            // AutoInclude en AppDbContext, pero que un PDF no reviente no
            // debería depender de una configuración que vive en otro archivo.
            // El cantón sí necesita el ThenInclude: a diferencia de Comunidad,
            // no está marcado AutoInclude. Se repite en ambos caminos (el
            // lote.Productora "histórico" y el de cada Cuy) porque el lote
            // legado sin cuyes vinculados solo llega por el primero.
            .Include(l => l.Productora).ThenInclude(p => p!.Comunidad)
                .ThenInclude(c => c.Canton)
            .Include(l => l.Novedades)
            .Include(l => l.Cuyes).ThenInclude(c => c.Productora)
                .ThenInclude(p => p!.Comunidad)
                .ThenInclude(c => c.Canton)
            .Include(l => l.Cuyes).ThenInclude(c => c.VentaLocalPago)
            .FirstOrDefaultAsync(l => l.CodigoLote == codigoLote)
            ?? throw new KeyNotFoundException($"Lote {codigoLote} no encontrado.");

        // Productoras que integran la jaula, con su aporte de animales.
        // Agrupado por Id (no por instancia) para que siga siendo correcto
        // aunque la consulta se materialice sin tracking
        var contribuyentes = lote.Cuyes
            .Where(c => c.Productora is not null)
            .GroupBy(c => c.ProductoraId)
            .Select(g => (Productora: g.First().Productora!, Cantidad: g.Count()))
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
                page.DefaultTextStyle(t => t
                    .FontSize(10)
                    .FontFamily(BrandingAssets.FamiliaTipografica));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        // Ancho fijo y no relativo: el isotipo es más alto que
                        // ancho, y dejarlo crecer con el contenido empujaría el
                        // código de lote fuera de una página A5.
                        row.ConstantItem(24).PaddingRight(6).AlignMiddle()
                            .Image(BrandingAssets.Logo).FitWidth();

                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("GUÍA DE MOVILIZACIÓN")
                                .FontSize(15).Bold().FontColor("#2E7D32");
                            c.Item().Text("COOPAGCUY — Cuy Azuayito")
                                .FontSize(10).FontColor("#555555");
                        });
                        row.ConstantItem(124).AlignRight().Column(c =>
                        {
                            c.Item().Text(lote.CodigoLote)
                                .FontSize(12).Bold().FontColor("#B71C1C");
                            c.Item().Text($"Emitida: {FechaUtc.FechaHoraLocal(DateTime.UtcNow)}")
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
                                    $"({productora.Comunidad.Nombre}, {productora.Comunidad.Canton.Nombre})");
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
                                $"Recepción: {FechaUtc.FechaHoraLocal(lote.FechaRecepcion)}");
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
                                    cols.RelativeColumn(4);    // Características
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
                                    tabla.Cell().PaddingVertical(1).PaddingRight(6)
                                        .Text(TextosGuia.Productora(cuy)).FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text($"{cuy.PesoGramos:F0}g").FontSize(7);
                                    tabla.Cell().PaddingVertical(1).PaddingRight(6)
                                        .Text(TextosGuia.Caracteristicas(cuy))
                                        .FontSize(7);
                                    tabla.Cell().PaddingVertical(1)
                                        .Text(cuy.Estado.ToString()).FontSize(7)
                                        .FontColor(colorEstado);
                                }
                            });
                        });
                    }

                    // Los animales que no viajaron. Van en la guía porque es
                    // el documento que acompaña al transporte: sin esto, la
                    // diferencia entre lo recibido y lo movilizado no tiene
                    // explicación en el propio papel.
                    var vendidos = lote.Cuyes
                        .Where(c => c.VentaLocalPagoId != null)
                        .OrderBy(c => c.NumeroEnLote)
                        .ToList();

                    if (vendidos.Count > 0)
                    {
                        col.Item().PaddingTop(8).Text("VENDIDOS EN LA COMUNIDAD")
                            .Bold();
                        col.Item().Text(
                            $"{vendidos.Count} de {lote.CantidadAnimales} animales " +
                            $"no viajaron a la planta:");

                        foreach (var cuy in vendidos)
                            col.Item().Text(TextosGuia.LineaVentaLocal(
                                cuy, cuy.VentaLocalPago!.FechaPago)).FontSize(9);
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
                                    $"Despacho: {FechaUtc.FechaHoraLocal(movilizacion.FechaDespacho)}");
                            });
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Cantidad movilizada: {movilizacion.CantidadMovilizada}");
                                r.RelativeItem().Text(
                                    $"Condiciones: {movilizacion.CondicionesTransporte ?? "-"}");
                            });

                            // Lo que NO se verificó. Va aquí y no en una nota
                            // al pie porque es el mismo dato que la línea de
                            // arriba, leído del otro lado: sin esto, una
                            // jaula que salió con tres casillas sin marcar
                            // produce una guía indistinguible de una completa.
                            var noVerificadas = TextosGuia.LineaNoVerificadas(
                                movilizacion.CondicionesClaves);

                            if (noVerificadas is not null)
                                c.Item().PaddingTop(2).Text(noVerificadas)
                                    .FontSize(9).Bold();

                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text(
                                    $"Forraje: {movilizacion.TipoForraje ?? "-"}");
                                r.RelativeItem().Text(
                                    TextoDeclaracionSanitaria(movilizacion));
                            });
                            if (movilizacion.FechaRecepcionPlanta is not null)
                            {
                                c.Item().Text(
                                    $"Recibido en planta: {FechaUtc.FechaHoraLocal(movilizacion.FechaRecepcionPlanta)} " +
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
