namespace RadarChollos.Models;

public class Producto
{
    public int Id { get; set; }

    /// <summary>
    /// Patrón de búsqueda con operadores (+, |, -, paréntesis). Ej: "ps5 - digital"
    /// </summary>
    public required string Patron { get; set; }

    /// <summary>
    /// Techo máximo de precio original configurado por el usuario.
    /// </summary>
    public decimal? PrecioMaximo { get; set; }

    /// <summary>
    /// Filtro opcional de tienda (ej: "amazon", "mediamarkt"). Null busca en cualquier tienda.
    /// </summary>
    public string? TiendaFiltro { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}