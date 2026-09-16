using FluentAssertions;
using Xunit;

namespace RadarChollos.Tests;

public class MatchingRuleTests
{
    [Theory]
    [InlineData("Consola PS5 Slim con dos mandos", "ps5 + mando", true)]
    [InlineData("Consola PS5 Slim sin accesorios", "ps5 + mando", false)]
    [InlineData("Mando DualSense PS5 blanco", "ps5 + funda", false)]
    public void CoincidePatron_DebeValidarTerminosRequeridos(string titulo, string patron, bool esperado)
    {
        var terminos = patron.Split('+').Select(t => t.Trim().ToLowerInvariant());
        var tituloNormalizado = titulo.ToLowerInvariant();

        bool resultado = terminos.All(t => tituloNormalizado.Contains(t));

        resultado.Should().Be(esperado);
    }

    [Theory]
    [InlineData("Sony PS5 Digital Edition", "-digital", false)]
    [InlineData("Sony PS5 Disco Edition", "-digital", true)]
    public void CoincideExclusion_DebeDescartarSiContieneTerminoVetado(string titulo, string patronExclusion, bool aceptado)
    {
        string vetado = patronExclusion.Replace("-", "").Trim().ToLowerInvariant();
        bool contieneVetado = titulo.ToLowerInvariant().Contains(vetado);

        bool resultado = !contieneVetado;

        resultado.Should().Be(aceptado);
    }

    [Theory]
    [InlineData(399.99, 450.00, true)]
    [InlineData(450.00, 450.00, true)]
    [InlineData(450.01, 450.00, false)]
    public void ValidarPrecioMaximo_DebeEvaluarLimiteCorrectamente(decimal precioOferta, decimal precioMaximo, bool esperado)
    {
        bool resultado = precioOferta <= precioMaximo;

        resultado.Should().Be(esperado);
    }
}