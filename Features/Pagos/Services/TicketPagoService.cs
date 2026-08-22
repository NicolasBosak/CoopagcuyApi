using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Branding;
using CoopagcuyApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CoopagcuyApi.Features.Pagos.Services;

public interface ITicketPagoService
{
    Task<byte[]> GenerarAsync(int pagoId);
}

/// <summary>
/// Comprobante impreso que la productora se lleva del centro de acopio.
///
/// Se genera con ancho continuo de 80 mm y alto variable porque el papel
/// térmico no tiene páginas: fijar un alto dejaría avances en blanco al final
/// de cada ticket, o cortaría el pie.
/// </summary>
public class TicketPagoService(AppDbContext db) : ITicketPagoService
{
    // Ancho del rollo. 80 mm es el estándar de las impresoras de recibos de
    // punto de venta; el contenido se compone para ~42 caracteres por línea.
    private const float AnchoMm = 80f;

    // Márgenes estrechos: el cabezal térmico no imprime en los bordes, pero
    // más de 3 mm desperdicia un ancho que ya es escaso.
    private const float MargenMm = 3f;

    /// <summary>
    /// Estado del pago tal y como se imprime. Público y estático para poder
    /// fijarlo por unidad: del binario del PDF no se puede afirmar nada.
    /// </summary>
    public static string TextoEstado(EstadoPago estado) => estado switch
    {
        EstadoPago.Pendiente => "PENDIENTE DE PAGO",
        EstadoPago.Pagado => "PAGADO — POR VERIFICAR",
        EstadoPago.Recibido => "PAGO VERIFICADO",
        _ => "ESTADO DESCONOCIDO"
    };

    /// <summary>
    /// Aclaración al pie. La productora se lleva este papel: si parece una
    /// factura, para ella lo será.
    /// </summary>
    public static string LeyendaLegal() =>
        "Este documento acredita un pago pendiente de la cooperativa. " +
        "No es una factura ni un comprobante tributario.";

    public async Task<byte[]> GenerarAsync(int pagoId)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pago = await db.Pagos
            .Include(p => p.Productora).ThenInclude(pr => pr.Comunidad)
            .Include(p => p.Lote)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        // Aporte de ESTA productora a la jaula, no el total: la jaula es
        // multi-productora y el ticket es de una sola.
        var cuyes = pago.LoteId is int loteId
            ? await db.CuyRegistros
                .Where(c => c.LoteId == loteId && c.ProductoraId == pago.ProductoraId)
                .Select(c => c.PesoGramos)
                .ToListAsync()
            : [];

        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(page =>
            {
                page.ContinuousSize(AnchoMm, Unit.Millimetre);
                page.Margin(MargenMm, Unit.Millimetre);
                // Liberation Sans: es la que instala Dockerfile.tests (y el de
                // producción) vía fonts-liberation. Una familia que no esté
                // instalada no da error — QuestPDF compone el PDF igual, pero
                // sin una sola letra dibujada.
                page.DefaultTextStyle(t => t
                    .FontSize(8)
                    .FontFamily(BrandingAssets.FamiliaTipografica));

                page.Content().Column(col =>
                {
                    col.Spacing(3);

                    col.Item().AlignCenter().Text("COOPAGCUY")
                        .FontSize(13).Bold();
                    col.Item().AlignCenter().Text("Comprobante de pago")
                        .FontSize(8);
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text($"Ticket N.º {pago.Id:D6}").Bold();
                    col.Item().Text(
                        $"Emitido: {pago.FechaPago:dd/MM/yyyy HH:mm}");
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text("PRODUCTORA").Bold();
                    col.Item().Text(pago.Productora.NombreCompleto);
                    col.Item().Text($"C.I. {pago.Productora.Cedula}");
                    col.Item().Text(pago.Productora.Comunidad.Nombre);
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text("LOTE").Bold();
                    col.Item().Text(pago.Lote?.CodigoLote ?? "—");
                    col.Item().Text(
                        $"Centro: {pago.Lote?.CentroAcopio.ToString() ?? "—"}");
                    col.Item().Text(
                        $"Recibido: {pago.Lote?.FechaRecepcion:dd/MM/yyyy}");
                    col.Item().Text($"Cuyes aportados: {cuyes.Count}");
                    col.Item().Text($"Peso total: {cuyes.Sum():N0} g");
                    col.Item().LineHorizontal(0.5f);

                    col.Item().AlignCenter().Text($"USD {pago.MontoUsd:N2}")
                        .FontSize(18).Bold();
                    col.Item().AlignCenter().Text(TextoEstado(pago.Estado))
                        .FontSize(9).Bold();

                    col.Item().LineHorizontal(0.5f);
                    col.Item().Text($"Responsable: {pago.Responsable}").FontSize(7);
                    col.Item().Text(LeyendaLegal()).FontSize(6);
                });
            });
        });

        return documento.GeneratePdf();
    }
}
