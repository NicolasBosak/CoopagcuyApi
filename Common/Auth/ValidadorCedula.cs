namespace CoopagcuyApi.Common.Auth;

// Validación de la cédula de identidad ecuatoriana:
//   · 10 dígitos numéricos
//   · código de provincia entre 01 y 24
//   · tercer dígito menor a 6 (6 y 9 corresponden a RUC, no a cédulas)
//   · dígito verificador por módulo 10 con coeficientes 2,1,2,1,2,1,2,1,2
//     (a los productos mayores a 9 se les resta 9)
public static class ValidadorCedula
{
    public static bool EsValida(string? cedula)
    {
        if (string.IsNullOrWhiteSpace(cedula)) return false;

        cedula = cedula.Trim();
        if (cedula.Length != 10 || !cedula.All(char.IsAsciiDigit)) return false;

        var provincia = int.Parse(cedula[..2]);
        if (provincia is < 1 or > 24) return false;

        if (cedula[2] - '0' > 5) return false;

        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var producto = (cedula[i] - '0') * (i % 2 == 0 ? 2 : 1);
            suma += producto > 9 ? producto - 9 : producto;
        }

        var verificador = (10 - suma % 10) % 10;
        return verificador == cedula[9] - '0';
    }
}
