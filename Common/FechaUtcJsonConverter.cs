using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopagcuyApi.Common;

/// <summary>
/// Serializa todo DateTime como instante UTC explícito ("…Z").
///
/// Sin esto, las horas del sistema se mostraban cinco horas en el futuro. La
/// cadena de causas es larga y ninguna de sus piezas es un error por separado:
///
///   1. Program.cs activa Npgsql.EnableLegacyTimestampBehavior, que hace que
///      las columnas "timestamp without time zone" devuelvan sus DateTime con
///      Kind=Unspecified.
///   2. System.Text.Json serializa un Unspecified SIN sufijo de zona:
///      "2026-08-18T21:51:00" en lugar de "2026-08-18T21:51:00Z".
///   3. En el navegador, new Date("2026-08-18T21:51:00") interpreta una fecha
///      sin zona como hora LOCAL. Así, un instante que era UTC se pintaba tal
///      cual: 21:51 en un equipo donde eran las 16:51.
///
/// El sistema guarda UTC en todas partes —FechaUtc.Normalizar lo garantiza en
/// la entrada, y el propio switch de Npgsql declara esa intención—, así que
/// interpretar un Unspecified como UTC al salir es coherente con el resto: se
/// está recuperando una zona que se perdió por el camino, no inventando una.
///
/// Al LEER se acepta cualquier formato ISO-8601 y se normaliza a UTC, igual
/// que hace FechaUtc.Normalizar con los cuerpos de las peticiones.
/// </summary>
public class FechaUtcJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(
        ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opciones) =>
        FechaUtc.Normalizar(reader.GetDateTime());

    public override void Write(
        Utf8JsonWriter writer, DateTime valor, JsonSerializerOptions opciones) =>
        writer.WriteStringValue(FechaUtc.Normalizar(valor));
}

/// <summary>
/// La versión nullable. System.Text.Json NO deriva el converter de un
/// DateTime? del converter de DateTime: sin esta clase, las fechas opcionales
/// —FechaRevocacion, FechaRecepcionPlanta, FechaCierre— seguirían saliendo sin
/// zona horaria mientras las obligatorias ya salían bien, que es la clase de
/// inconsistencia que cuesta semanas de descubrir.
/// </summary>
public class FechaUtcNullableJsonConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(
        ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opciones) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : FechaUtc.Normalizar(reader.GetDateTime());

    public override void Write(
        Utf8JsonWriter writer, DateTime? valor, JsonSerializerOptions opciones)
    {
        if (valor is null) writer.WriteNullValue();
        else writer.WriteStringValue(FechaUtc.Normalizar(valor.Value));
    }
}
