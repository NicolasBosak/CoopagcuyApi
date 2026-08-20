using CoopagcuyApi.Common;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Fija los tres parámetros de recepción. No comprueban lógica: existen para
/// que un cambio accidental de un número de negocio rompa CI en vez de
/// desplegarse en silencio. El front mantiene su propio espejo en
/// src/domain/reglasRecepcion.ts y estos valores deben coincidir.
/// </summary>
public class ReglasRecepcionTests
{
    [Fact]
    public void LaJaulaAdmiteQuinceCuyes() =>
        ReglasRecepcion.CapacidadJaula.ShouldBe(15);

    [Fact]
    public void ElPesoMinimoEsMilDoscientosGramos() =>
        ReglasRecepcion.PesoMinimoGramos.ShouldBe(1200m);

    [Fact]
    public void ElPesoMaximoEsMilQuinientosGramos() =>
        ReglasRecepcion.PesoMaximoGramos.ShouldBe(1500m);
}
