using CoopagcuyApi.Features.Reportes.Services;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// El costo de lo vendido no se estima: se rastrea animal por animal hasta el
/// pago de su productora, y se reparte entre los animales que ese pago cubrió.
///
/// Lo que no se puede saber —un animal cuya productora todavía no ha
/// cobrado— vale DESCONOCIDO, no cero. Un margen calculado ignorando eso
/// sería optimista justo cuando más falta pagar.
/// </summary>
public class CostoDeLoDespachadoTests
{
    [Fact]
    public void ReparteElPagoEntreLosAnimalesQueCubrio()
    {
        // Una productora cobró 120 por 12 cuyes; se despacharon 3.
        var animales = new[]
        {
            new AnimalDespachado(LoteId: 1, NumeroEnLote: 1, ProductoraId: 7),
            new AnimalDespachado(1, 2, 7),
            new AnimalDespachado(1, 3, 7),
        };
        var pagos = new[] { new PagoDeLote(1, 7, 120m, AnimalesCubiertos: 12) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(30m);
        costo.AnimalesSinCosto.ShouldBe(0);
    }

    [Fact]
    public void UnAnimalSinPagoNoValeCero()
    {
        // Su productora todavía no ha cobrado ese lote. El reporte lo declara,
        // no lo rellena.
        var animales = new[]
        {
            new AnimalDespachado(1, 1, 7),
            new AnimalDespachado(2, 1, 9),   // sin pago
        };
        var pagos = new[] { new PagoDeLote(1, 7, 100m, 10) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(10m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void UnPagoQueNoCubrioAnimalesNoDivideEntreCero()
    {
        var animales = new[] { new AnimalDespachado(1, 1, 7) };
        var pagos = new[] { new PagoDeLote(1, 7, 100m, AnimalesCubiertos: 0) };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(0m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void UnAnimalSinProductoraSeCuentaComoSinCosto()
    {
        // Jaula antigua sin detalle por animal: no se sabe de quién era.
        var animales = new[] { new AnimalDespachado(1, 1, ProductoraId: null) };

        var costo = CostoDeLoDespachado.Calcular(animales, []);

        costo.Total.ShouldBe(0m);
        costo.AnimalesSinCosto.ShouldBe(1);
    }

    [Fact]
    public void CadaAnimalSeAtribuyeAlPagoDeSuPropiaProductora()
    {
        // Jaula compartida: dos productoras en el mismo lote, con pagos
        // distintos. Atribuir todo al primero sería el error fácil.
        var animales = new[]
        {
            new AnimalDespachado(1, 1, 7),
            new AnimalDespachado(1, 9, 8),
        };
        var pagos = new[]
        {
            new PagoDeLote(1, 7, 100m, 10),   // 10 por animal
            new PagoDeLote(1, 8, 40m, 4),     // 10 por animal… pero de otra
        };

        var costo = CostoDeLoDespachado.Calcular(animales, pagos);

        costo.Total.ShouldBe(20m);
        costo.AnimalesSinCosto.ShouldBe(0);
    }
}
