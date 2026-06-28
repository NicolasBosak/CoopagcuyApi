using CoopagcuyApi.Features.Recepcion.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Recepcion.Validators;

public class RegistrarLoteValidator : AbstractValidator<RegistrarLoteDto>
{
    // Colores aceptados para mercado formal — SRS Apéndice 5.1
    private static readonly string[] ColoresAceptados =
        ["Blanco", "Bayo", "Plomo", "Combinado"];

    public RegistrarLoteValidator()
    {
        RuleFor(x => x.ProductoraId)
            .GreaterThan(0)
            .WithMessage("Debe seleccionar una productora válida.");

        RuleFor(x => x.CantidadAnimales)
            .InclusiveBetween(1, 20)
            .WithMessage("Un lote debe tener entre 1 y 20 animales (RF-104).");

        RuleFor(x => x.PesoTotalGramos)
            .GreaterThan(0)
            .WithMessage("El peso total debe ser mayor a cero.");

        RuleFor(x => x.FechaRecepcion)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de recepción no puede ser futura.");

        RuleFor(x => x.EstadoOreja)
            .Must(e => new[] { "Blanda", "Semiblanda", "Dura" }.Contains(e))
            .WithMessage("Estado de oreja inválido. Use: Blanda, Semiblanda o Dura.");

        RuleFor(x => x.ColorPelaje)
            .NotEmpty()
            .WithMessage("El color del pelaje es obligatorio.");

        RuleFor(x => x.ResponsableRecepcion)
            .NotEmpty()
            .WithMessage("El responsable de recepción es obligatorio.");
    }
}