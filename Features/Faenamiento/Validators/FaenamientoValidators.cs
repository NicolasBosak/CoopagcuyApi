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
        RuleFor(x => x.LoteFaenadoId)
            .GreaterThan(0)
            .WithMessage("Debe seleccionar un lote faenado válido.");

        RuleFor(x => x.CuyFaenamientoIds)
            .NotEmpty()
            .WithMessage("Selecciona al menos un animal para despachar.");

        RuleFor(x => x.ClienteDestino)
            .NotEmpty()
            .WithMessage("El cliente de destino es obligatorio.")
            .MaximumLength(200);

        RuleFor(x => x.Responsable)
            .NotEmpty()
            .WithMessage("El responsable del despacho es obligatorio.");

        RuleFor(x => x.TipoMercado)
            .Must(t => t is "Local" or "Nacional" or "Internacional")
            .WithMessage("El mercado de destino debe ser Local, Nacional o Internacional.");

        RuleFor(x => x.FechaDespacho)
            .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de despacho no puede ser futura.");
    }
}