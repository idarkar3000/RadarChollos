namespace RadarChollos.Models
{
    public class Producto
    {
        public int Id { get; set; }

        // Patrón de búsqueda con operadores (+, |, -, paréntesis). Ej: "ps5 - digital"
        public required string Patron { get; set; }

        public decimal? PrecioMaximo { get; set; }
        public string? TiendaFiltro { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    }
}
