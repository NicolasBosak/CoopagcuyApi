namespace CoopagcuyApi.Features.Catalogos.Models;

/// <summary>
/// División política de primer nivel. Existe para que la organización pueda
/// crecer fuera de Azuay: antes la provincia estaba escrita a mano en la
/// página pública del QR y en la guía de movilización.
/// </summary>
public class Provincia
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Activa { get; set; } = true;

    public ICollection<Canton> Cantones { get; set; } = [];
}
