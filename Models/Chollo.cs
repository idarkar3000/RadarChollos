namespace RadarChollos.Models;

public class Chollo
{
    public int Id { get; set; }

    public required string Titulo { get; set; }

    /// <summary>
    /// URL única de la oferta. Evita duplicados mediante un índice único en BD.
    /// </summary>
    public required string Enlace { get; set; }

    public decimal? Precio { get; set; }

    /// <summary>
    /// Tienda detectada (Amazon, MediaMarkt, Carrefour, PcComponentes, etc.)
    /// </summary>
    public string? Tienda { get; set; }

    public DateTime FechaDeteccion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Clave foránea que relaciona la oferta con la regla que la cazó.
    /// </summary>
    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }
}