using Xunit;

namespace CoopagcuyApi.Tests.Infra;

/// <summary>
/// Todas las clases de prueba comparten una sola <see cref="ApiFactory"/>.
/// Esto además las serializa, que es obligatorio: comparten una única base de
/// datos y correrlas en paralelo haría que la limpieza de una borrase los
/// datos de otra.
/// </summary>
[CollectionDefinition(Nombre)]
public class ColeccionApi : ICollectionFixture<ApiFactory>
{
    public const string Nombre = "api";
}
