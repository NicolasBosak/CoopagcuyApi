using CoopagcuyApi.Common.Auth;
using CoopagcuyApi.Features.Productoras.DTOs;
using CoopagcuyApi.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CoopagcuyApi.Features.Productoras.Validators;

public class CrearProductoraValidator : AbstractValidator<CrearProductoraDto>
{
    public CrearProductoraValidator(AppDbContext db)
    {
        RuleFor(x => x.NombreCompleto)
            .NotEmpty().WithMessage("El nombre completo es obligatorio.")
            .MaximumLength(150);

        // Misma validación que en el login: algoritmo ecuatoriano completo
        // (provincia y dígito verificador), no solo longitud
        RuleFor(x => x.Cedula)
            .NotEmpty().WithMessage("La cédula es obligatoria.")
            .Must(ValidadorCedula.EsValida)
            .WithMessage("El número de cédula no es válido.");

        // La comunidad debe existir en el catálogo y estar activa: es lo que
        // impide que un typo cree una comunidad fantasma aguas abajo
        RuleFor(x => x.ComunidadId)
            .GreaterThan(0).WithMessage("La comunidad es obligatoria.")
            .MustAsync(async (id, ct) =>
                await db.Comunidades.AnyAsync(c => c.Id == id && c.Activa, ct))
            .WithMessage("La comunidad seleccionada no existe o está inactiva.");
    }
}
