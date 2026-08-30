using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Catálogo gestionable de comunidades — RF-102 / RF-506.
///
/// La comunidad cuelga de un cantón del catálogo, y el cantón de una
/// provincia: antes el cantón era texto libre aquí dentro y "Nabón" y
/// "Nabon" acababan siendo dos cantones distintos.
///
/// El CAT de referencia NO está restringido por geografía: una comunidad
/// entrega en el centro que le queda más cerca, aunque esté en otra
/// provincia. La procedencia del cuy sale de la comunidad, no del CAT.
/// </summary>
public class Comunidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    public int CantonId { get; set; }
    public Canton Canton { get; set; } = null!;

    public CentroAcopio CatReferencia { get; set; }
    public bool Activa { get; set; } = true;

    // Ubicación en el mapa público. Nullable porque una comunidad dada de
    // alta desde Administración nace sin coordenadas y la ficha del QR
    // tiene que seguir funcionando sin ellas.
    public decimal? Latitud { get; set; }
    public decimal? Longitud { get; set; }

    // La altitud es un rango, no un punto: una comunidad ocupa una ladera.
    // Cuando la cooperativa da una sola cifra, mínimo y máximo coinciden.
    public int? AltitudMinM { get; set; }
    public int? AltitudMaxM { get; set; }
}
