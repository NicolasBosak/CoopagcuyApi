using CoopagcuyApi.Common;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Los documentos se imprimían con la hora UTC cruda: un faenamiento
/// registrado a las 15:30 salía en el informe como las 20:30. Todo lo que se
/// persiste sigue siendo UTC —eso está bien—; lo que cambia es cómo se
/// traduce en el momento de imprimirlo.
/// </summary>
public class FechaLocalTests
{
    [Fact]
    public void ALocal_restaLasCincoHorasDelPiloto()
    {
        var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        FechaUtc.ALocal(utc).ShouldBe(new DateTime(2026, 8, 21, 15, 30, 0));
    }

    [Fact]
    public void ALocal_cruzaElDiaHaciaAtras()
    {
        // Las 02:00 UTC del 22 son las 21:00 del 21 en el CAT. Es el caso que
        // hacía que un registro de la tarde apareciera fechado al día
        // siguiente, no solo con la hora corrida.
        var utc = new DateTime(2026, 8, 22, 2, 0, 0, DateTimeKind.Utc);

        FechaUtc.FechaHoraLocal(utc).ShouldBe("21/08/2026 21:00");
    }

    [Fact]
    public void ALocal_trataUnKindNoEspecificadoComoUtc()
    {
        // Un valor que no venga de Npgsql llega como Unspecified. Se
        // interpreta como UTC y no como hora del servidor, que en un
        // contenedor puede estar en cualquier zona.
        var sinZona = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Unspecified);

        FechaUtc.ALocal(sinZona).ShouldBe(new DateTime(2026, 8, 21, 15, 30, 0));
    }

    [Fact]
    public void FechaLocal_omiteLaHora()
    {
        var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);

        FechaUtc.FechaLocal(utc).ShouldBe("21/08/2026");
    }

    [Fact]
    public void FechaHoraLocal_sinFechaDevuelveGuion()
    {
        // Interpolar un DateTime? nulo produce cadena vacía, y en el papel eso
        // deja un renglón que dice "Recibido:" y nada más: indistinguible de
        // un fallo de maquetación.
        FechaUtc.FechaHoraLocal(null).ShouldBe("—");
    }

    [Fact]
    public void FechaLocal_sinFechaDevuelveGuion()
    {
        FechaUtc.FechaLocal(null).ShouldBe("—");
    }

    [Fact]
    public void ElFormatoNoDependeDeLaCulturaDeLaMaquina()
    {
        // "dd/MM/yyyy" usa el separador de fecha de la cultura activa, no una
        // barra literal. Sin CultureInfo.InvariantCulture, la misma línea sale
        // distinta según dónde corra el contenedor.
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var utc = new DateTime(2026, 8, 21, 20, 30, 0, DateTimeKind.Utc);
            FechaUtc.FechaLocal(utc).ShouldBe("21/08/2026");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }
}
