using CoopagcuyApi.Features.Faenamiento.DTOs;
using FluentValidation;

namespace CoopagcuyApi.Features.Faenamiento.Validators;

public class RegistrarFaenamientoValidator : AbstractValidator<RegistrarFaenamientoDto>
{
    public RegistrarFaenamientoValidator()
    {
        RuleFor(x => x.LoteId)
            .GreaterThan(0)
            .WithMessage("Debe seleccionar un lote válido.");

        RuleFor(x => x.OperarioResponsable)
            .NotEmpty()
            .WithMessage("El operario responsable es obligatorio.");

        RuleFor(x => x.UnidadesFaenadas)
            .GreaterThan(0)
            .WithMessage("Las unidades faenadas deben ser mayor a cero.");

        RuleFor(x => x.PesoTotalCanalGramos)
            .GreaterThan(0)
            .WithMessage("El peso total de canal debe ser mayor a cero.");

        RuleFor(x => x.FechaFaenamiento)
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de faenamiento no puede ser futura.");

        // Alerta si el peso promedio de canal es menor a 880g — SRS RF-302
        RuleFor(x => x)
            .Must(x => x.UnidadesFaenadas == 0 ||
                       (x.PesoTotalCanalGramos / x.UnidadesFaenadas) >= 880)
            .WithMessage(x =>
            {
                var promedio = x.UnidadesFaenadas > 0
                    ? x.PesoTotalCanalGramos / x.UnidadesFaenadas
                    : 0;
                return $"Peso promedio de canal ({promedio:F0}g) por debajo de 880g. " +
                       "Verifique el faenamiento antes de continuar.";
            });
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
            .LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5))
            .WithMessage("La fecha de despacho no puede ser futura.");
    }
}