using System.Globalization;
using CoopagcuyApi.Common;
using CoopagcuyApi.Common.Branding;
using CoopagcuyApi.Features.Pagos.Models;
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

    /// <summary>
    /// Peso de los cuyes que ESTE ticket cuenta como "aportados". Aporte de
    /// ESTA productora a la jaula, no el total: la jaula es multi-productora
    /// y el ticket es de una sola.
    ///
    /// Y dentro de ese aporte, la mitad que le corresponde a ESTE ticket: una
    /// venta local cobra los animales que se quedaron en la comunidad, así
    /// que "cuyes aportados" ahí son los vendidos en esta venta
    /// (VentaLocalPagoId == este pago), no los quince de la jaula. El pago de
    /// la planta, en cambio, cobra lo que SÍ viajó: los que no se vendieron
    /// localmente (VentaLocalPagoId == null). Sumar ambos conjuntos da el
    /// total histórico de la jaula, que es exactamente lo que el ticket NO
    /// debe imprimir cuando hay una venta parcial de por medio — es el
    /// motivo original de este arreglo (15 cuando se pagaron 12).
    ///
    /// Público —y no solo un detalle de GenerarAsync— porque del binario del
    /// PDF no se puede afirmar el conteo (ver comentario de clase): esta es
    /// la única forma de fijarlo por unidad.
    /// </summary>
    public async Task<List<decimal>> ObtenerPesosCuyesDelTicketAsync(Pago pago) =>
        pago.LoteId is int loteId
            ? await db.CuyRegistros
                .Where(c => c.LoteId == loteId && c.ProductoraId == pago.ProductoraId
                    && (pago.EsVentaLocal
                        ? c.VentaLocalPagoId == pago.Id
                        : c.VentaLocalPagoId == null))
                .Select(c => c.PesoGramos)
                .ToListAsync()
            : [];

    public async Task<byte[]> GenerarAsync(int pagoId)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pago = await db.Pagos
            .Include(p => p.Productora).ThenInclude(pr => pr.Comunidad)
            .Include(p => p.Lote)
            .Include(p => p.Descuentos).ThenInclude(d => d.NovedadCat)
                .ThenInclude(n => n.CuyRegistro)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pagoId)
            ?? throw new KeyNotFoundException($"Pago con Id {pagoId} no encontrado.");

        var cuyes = await ObtenerPesosCuyesDelTicketAsync(pago);

        // Números de los animales que cubre esta venta. Van al papel para que
        // la productora pueda contrastarlos con los que entregó.
        var vendidos = pago.EsVentaLocal
            ? await db.CuyRegistros
                .Where(c => c.VentaLocalPagoId == pago.Id)
                .OrderBy(c => c.NumeroEnLote)
                .Select(c => c.NumeroEnLote)
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
                    col.Item().AlignCenter().Text(TextosVentaLocal.Encabezado(pago))
                        .FontSize(8);
                    col.Item().LineHorizontal(0.5f);

                    col.Item().Text($"Ticket N.º {pago.Id:D6}").Bold();
                    col.Item().Text(
                        $"Emitido: {FechaUtc.FechaHoraLocal(pago.FechaPago)}");
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
                        $"Recibido: {FechaUtc.FechaLocal(pago.Lote?.FechaRecepcion)}");
                    col.Item().Text($"Cuyes aportados: {cuyes.Count}");
                    col.Item().Text($"Peso total: {cuyes.Sum():N0} g");
                    col.Item().LineHorizontal(0.5f);

                    if (pago.EsVentaLocal)
                    {
                        col.Item().Text("ANIMALES VENDIDOS").Bold();
                        col.Item().Text(string.Join(", ",
                            vendidos.Select(n => $"#{n}")));
                        col.Item().LineHorizontal(0.5f);
                    }

                    if (TextosTicket.HayDesglose(pago))
                    {
                        // InvariantCulture en las tres cifras del ticket, por
                        // el mismo motivo que en FechaUtc: "N2" usa el
                        // separador decimal de la cultura activa del
                        // contenedor, y la productora no puede recibir un
                        // monto que cambie de forma según dónde corra el API.
                        col.Item().Text(string.Create(CultureInfo.InvariantCulture,
                            $"Subtotal: USD {pago.MontoUsd:N2}"));
                        col.Item().PaddingTop(2).Text("DESCUENTOS").Bold();

                        // Orden por Id: dos reimpresiones del mismo ticket
                        // tienen que salir iguales.
                        foreach (var descuento in pago.Descuentos.OrderBy(d => d.Id))
                        {
                            col.Item().Text(TextosTicket.LineaNovedad(descuento));

                            // Sin truncar. Es el motivo por el que se le pagó
                            // menos: media frase la deja sin poder reclamar, y
                            // eso es peor que no imprimirlo. QuestPDF envuelve
                            // solo dentro del ancho del rollo.
                            col.Item().PaddingLeft(4)
                                .Text(descuento.Descripcion).FontSize(7);

                            col.Item().AlignRight()
                                .Text(string.Create(CultureInfo.InvariantCulture,
                                    $"-USD {descuento.MontoUsd:N2}"));
                        }

                        col.Item().LineHorizontal(0.5f);
                    }

                    col.Item().AlignCenter()
                        .Text(string.Create(CultureInfo.InvariantCulture,
                            $"USD {TextosTicket.MontoDestacado(pago):N2}"))
                        .FontSize(18).Bold();
                    col.Item().AlignCenter().Text(TextosVentaLocal.TextoEstado(pago))
                        .FontSize(9).Bold();

                    // Condicionada a la venta local: en el ciclo con la
                    // planta el método es siempre transferencia (no hay otro
                    // que distinguir desde el proyecto anterior), así que
                    // nombrarlo no informa nada y sería ruido en un ticket
                    // que ya está en uso. En una venta local sí importa —es
                    // justo lo que la productora necesita ver, sobre todo si
                    // fue a cuotas.
                    if (pago.EsVentaLocal)
                        col.Item().AlignCenter()
                            .Text(TextosVentaLocal.LineaMetodo(pago)).FontSize(8);

                    col.Item().LineHorizontal(0.5f);
                    col.Item().Text($"Responsable: {pago.Responsable}").FontSize(7);
                    col.Item().Text(LeyendaLegal()).FontSize(6);
                });
            });
        });

        return documento.GeneratePdf();
    }
}
