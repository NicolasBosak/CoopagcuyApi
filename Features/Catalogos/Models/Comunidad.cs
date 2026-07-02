using CoopagcuyApi.Common;

namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Catálogo gestionable de comunidades — RF-102 / RF-506.
/// Cada comunidad pertenece a un cantón y tiene un CAT de referencia.
/// Los centros de acopio (5 en el piloto) siguen siendo un enum fijo
/// porque su código forma parte del identificador de lote (CAT-AAAAMMDD-SEC).
/// </summary>
public class Comunidad
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Canton { get; set; } = string.Empty;
    public CentroAcopio CatReferencia { get; set; }
    public bool Activa { get; set; } = true;
}
