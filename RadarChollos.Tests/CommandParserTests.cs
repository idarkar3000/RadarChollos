using FluentAssertions;
using Xunit;

namespace RadarChollos.Tests;

public class CommandParserTests
{
    [Fact]
    public void ParsearComandoAdd_DebeExtraerCamposCorrectamente()
    {
        string input = "/add rtx 4070 -ti 550 @pccomponentes";
        var partes = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        string? tienda = partes.FirstOrDefault(p => p.StartsWith('@'))?.Replace("@", "");
        if (tienda != null) partes.Remove("@" + tienda);

        decimal? precio = null;
        var partePrecio = partes.LastOrDefault();
        if (decimal.TryParse(partePrecio, out decimal precioParseado))
        {
            precio = precioParseado;
            partes.RemoveAt(partes.Count - 1);
        }

        string patron = string.Join(" ", partes);

        patron.Should().Be("rtx 4070 -ti");
        precio.Should().Be(550m);
        tienda.Should().Be("pccomponentes");
    }

    [Fact]
    public void ParsearComandoAdd_SinPrecioNiTienda_DebeExtraerSoloPatron()
    {
        string input = "/add monitor dell";
        var partes = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        string patron = string.Join(" ", partes);

        patron.Should().Be("monitor dell");
    }
}