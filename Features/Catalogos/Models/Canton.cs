namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// Cantón al que pertenece una comunidad. Antes era un string libre dentro
/// de Comunidad, y con texto libre "Nabón" y "Nabon" eran dos cantones
/// distintos — la misma cicatriz que ya obligó a sacar el cantón de
/// Productora y llevarlo al catálogo de comunidades.
/// </summary>
public class Canton
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    public int ProvinciaId { get; set; }
    public Provincia Provincia { get; set; } = null!;

    public bool Activo { get; set; } = true;
}
