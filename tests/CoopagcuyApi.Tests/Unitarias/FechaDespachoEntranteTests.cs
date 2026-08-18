using System.Text.Json;
using CoopagcuyApi.Common;
using CoopagcuyApi.Features.Faenamiento.DTOs;
using Shouldly;
using Xunit;

namespace CoopagcuyApi.Tests.Unitarias;

/// <summary>
/// Diagnóstico del reporte de Salida (Fase 0). ReporteSalidaTests ya demostró
/// que la CONSULTA del reporte encuentra un despacho de hoy dentro del rango
/// del mes en curso, así que el fallo no está ahí. La otra mitad del recorrido
/// es la fecha que entra por el cuerpo de la petición: si llegara desplazada,
/// el despacho quedaría guardado con una fecha que el rango no cubre.
///
/// El front envía `new Date(fechaDespacho).toISOString()`, es decir ISO-8601
/// con sufijo Z. Estas pruebas comprueban ese formato exacto contra el DTO
/// real, con las mismas opciones de serialización que usa ASP.NET Core.
/// </summary>
public class FechaDespachoEntranteTests
{
    // Las mismas que aplica ASP.NET Core a los cuerpos JSON: nombres en
    // camelCase e insensible a mayúsculas.
    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void UnaFechaISOConZ_llegaComoUtcYNoSeDesplaza()
    {
        var cuerpo = """
        {
            "loteFaenadoId": 1,
            "cuyFaenamientoIds": [1],
            "clienteDestino": "Mercado de Cuenca",
            "fechaDespacho": "2026-08-18T11:09:00.000Z",
            "responsable": "Nicolas Nieves"
        }
        """;

        var dto = JsonSerializer.Deserialize<RegistrarDespachoDto>(cuerpo, Opciones);
        dto.ShouldNotBeNull();

        var normalizada = FechaUtc.Normalizar(dto.FechaDespacho);

        normalizada.Kind.ShouldBe(DateTimeKind.Utc);
        normalizada.ShouldBe(new DateTime(2026, 8, 18, 11, 9, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void UnaFechaConDesfaseHorario_seConvierteAUtcSinPerderElInstante()
    {
        // Ecuador es UTC-5: las 06:09 locales son las 11:09 UTC. Si el cliente
        // mandara el desfase en vez de la Z, el instante debe ser el mismo.
        var cuerpo = """
        {
            "loteFaenadoId": 1,
            "cuyFaenamientoIds": [1],
            "clienteDestino": "Mercado de Cuenca",
            "fechaDespacho": "2026-08-18T06:09:00.000-05:00",
            "responsable": "Nicolas Nieves"
        }
        """;

        var dto = JsonSerializer.Deserialize<RegistrarDespachoDto>(cuerpo, Opciones);
        dto.ShouldNotBeNull();

        var normalizada = FechaUtc.Normalizar(dto.FechaDespacho);

        normalizada.Kind.ShouldBe(DateTimeKind.Utc);
        normalizada.ShouldBe(new DateTime(2026, 8, 18, 11, 9, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void UnaFechaSinZonaHoraria_seInterpretaComoUtcYNoComoHoraDelServidor()
    {
        // Un cliente que omita la zona llegaría como Unspecified. Se interpreta
        // como UTC a propósito: la zona del contenedor no debe influir en el
        // dato guardado.
        var cuerpo = """
        {
            "loteFaenadoId": 1,
            "cuyFaenamientoIds": [1],
            "clienteDestino": "Mercado de Cuenca",
            "fechaDespacho": "2026-08-18T11:09:00",
            "responsable": "Nicolas Nieves"
        }
        """;

        var dto = JsonSerializer.Deserialize<RegistrarDespachoDto>(cuerpo, Opciones);
        dto.ShouldNotBeNull();

        var normalizada = FechaUtc.Normalizar(dto.FechaDespacho);

        normalizada.Kind.ShouldBe(DateTimeKind.Utc);
        normalizada.ShouldBe(new DateTime(2026, 8, 18, 11, 9, 0, DateTimeKind.Utc));
    }
}
