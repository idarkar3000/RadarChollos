namespace RadarChollos.Models
{
    public class Chollo
    {
        public int Id { get; set; }
        public required string Titulo { get; set; }
        public required string Enlace { get; set; }
        public decimal? Precio { get; set; }
        public string? Tienda { get; set; }
        public DateTime FechaDeteccion { get; set; } = DateTime.UtcNow;
        public int? ProductoId { get; set; }
        public Producto? Producto { get; set; }
    }
}
