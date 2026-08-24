using CoopagcuyApi.Features.Productoras.Models;

namespace CoopagcuyApi.Features.Recepcion.Models;

/// <summary>
/// Registro de movilización del lote desde el CAT hasta la planta de
/// faenamiento — eslabón 2 del modelo de trazabilidad (transporte).
/// Cierra el quiebre documental identificado en el diagnóstico: sin este
/// registro la trazabilidad se pierde durante el traslado.
/// </summary>
public class Movilizacion
{
    public int Id { get; set; }

    // Un lote se moviliza una sola vez hacia la planta (1:1)
    public int LoteId { get; set; }
    public Lote Lote { get; set; } = null!;

    public DateTime FechaDespacho { get; set; }
    public string Conductor { get; set; } = string.Empty;
    public int CantidadMovilizada { get; set; }
    public string? CondicionesTransporte { get; set; }

    // Declaración de tratamientos básicos (guía de movilización)
    public string? TipoForraje { get; set; }

    // Legado: se dejó de capturar en 2026-08, sustituido por la declaración
    // de abajo. La columna se conserva para que reimprimir una guía antigua
    // no pierda el dato.
    public int? DiasRetiroMedicamentos { get; set; }

    // Nulo = movilización anterior al cambio, nunca se preguntó.
    // True = el responsable declaró que no recibieron antibióticos en 7 días.
    // El validador exige true en los registros nuevos, así que false no
    // debería aparecer nunca; se admite por no mentirle al tipo.
    public bool? SinAntibioticos7Dias { get; set; }

    public string ResponsableDespacho { get; set; } = string.Empty;
    public string? Observaciones { get; set; }

    // Confirmación de llegada a la planta de Sulupali Chico
    public DateTime? FechaRecepcionPlanta { get; set; }
    public string? RecibidoPor { get; set; }
    public string? CondicionLlegada { get; set; }

    // Claves del checklist que SÍ se marcaron, separadas por punto y coma.
    //
    // CondicionesTransporte guarda una frase ya compuesta y pierde las
    // claves, así que con ella sola es imposible saber qué faltó: habría que
    // parsear texto, y el propio catálogo advierte de que las etiquetas
    // cambian mientras las claves no ("Maximo20" sigue llamándose así aunque
    // el tope sea 15).
    //
    // NULO significa "movilización anterior a este cambio, no se registró",
    // que NO es lo mismo que "no se verificó ninguna". La guía distingue los
    // dos casos.
    public string? CondicionesClaves { get; set; }

    // Respuesta a "¿llegaron en buen estado?". Nula en las movilizaciones
    // anteriores, en las que nunca se preguntó. Obligatoria en el servicio
    // cuando el checklist de transporte salió incompleto.
    public bool? LlegaronEnBuenEstado { get; set; }

    // Claves del cuestionario de llegada, separadas por punto y coma. Solo
    // se llenan cuando LlegaronEnBuenEstado es false.
    //
    // La observación libre sigue viviendo en CondicionLlegada, que ya era
    // texto libre: reutilizarla mantiene legibles las recepciones antiguas
    // en vez de dejar una columna con dos significados.
    public string? CondicionesLlegadaClaves { get; set; }

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
