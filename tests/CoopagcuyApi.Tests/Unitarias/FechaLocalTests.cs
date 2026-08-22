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

        var local = FechaUtc.ALocal(utc);

        local.ShouldBe(new DateTime(2026, 8, 21, 15, 30, 0));

        // Decisión deliberada y documentada en FechaUtc.ALocal: el resultado
        // se marca Unspecified porque ya no es un instante UTC, y dejarlo
        // marcado como Utc invitaría a que alguien lo volviera a convertir
        // más abajo. ShouldBe(DateTime) compara solo los ticks e ignora el
        // Kind, así que sin esta aserción nadie fija esa decisión.
        local.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Fact]
    public void Normalizar_convierteUnKindLocalSegunLaZonaDelSistema()
    {
        // La única rama de Normalizar con efecto real es DateTimeKind.Local.
        // TZ=America/Guayaquil se fija en docker-compose.tests.yml para que
        // esta prueba pueda fallar: en un contenedor sin esa variable corre
        // en UTC, donde Local == Utc y esta rama sería indistinguible de la
        // de arriba, con o sin la conversión.
        var local = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Local);

        var normalizado = FechaUtc.Normalizar(local);

        normalizado.ShouldBe(new DateTime(2026, 8, 21, 14, 0, 0));
        normalizado.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void ALocal_normalizaUnKindLocalAntesDeRestarElDesfase()
    {
        // Con el sistema en America/Guayaquil —el mismo desfase que
        // DesfasePiloto— la ida (Local a Utc) y la vuelta (Utc a hora del
        // piloto) se cancelan: la hora de reloj no cambia. Si alguien
        // quitara la conversión de la rama Local de Normalizar, 09:00 se
        // trataría como si ya fuera UTC y el resultado saldría 04:00.
        var local = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Local);

        FechaUtc.ALocal(local).ShouldBe(new DateTime(2026, 8, 21, 9, 0, 0));
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

            // Mismo riesgo en FechaHoraLocal, pero con el separador de HORA:
            // ":" es el marcador de hora de un formato personalizado igual
            // que "/" lo es de fecha. Quitar InvariantCulture solo de este
            // método no lo detectaría la aserción de arriba, que no lo llama.
            FechaUtc.FechaHoraLocal(utc).ShouldBe("21/08/2026 15:30");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = anterior;
        }
    }
}
