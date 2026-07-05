using CoopagcuyApi.Features.Faenamiento.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Faenamiento.Validators;

public class RegistrarFaenamientoBatchValidator
    : AbstractValidator<RegistrarFaenamientoBatchDto>
{
    public RegistrarFaenamientoBatchValidator()
    {
        RuleFor(x => x.OperarioResponsable)
            .NotEmpty()
            .WithMessage("El operario responsable es obligatorio.");

        // El límite se evalúa en cada validación, no al construir el
        // validador (evita congelar la hora de arranque)
        RuleFor(x => x.FechaFaenamiento)
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de faenamiento no puede ser futura.");

        RuleFor(x => x.Lotes)
            .NotEmpty()
            .WithMessage("Selecciona al menos un lote para faenar.");
    }
}

public class RegistrarDespachoValidator : AbstractValidator<RegistrarDespachoDto>
{
    public RegistrarDespachoValidator()
    {
        RuleFor(x => x.LoteId)
            .GreaterThan(0)
            .WithMessage("Debe seleccionar un lote válido.");

        RuleFor(x => x.ClienteDestino)
            .NotEmpty()
            .WithMessage("El cliente de destino es obligatorio.")
            .MaximumLength(200);

        RuleFor(x => x.CantidadUnidades)
            .GreaterThan(0)
            .WithMessage("La cantidad de unidades debe ser mayor a cero.");

        RuleFor(x => x.Responsable)
            .NotEmpty()
            .WithMessage("El responsable del despacho es obligatorio.");

        RuleFor(x => x.FechaDespacho)
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de despacho no puede ser futura.");
    }
}