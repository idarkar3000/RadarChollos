namespace RadarChollos.Services;

/// <summary>
/// Representa una oferta procesada del feed RSS con sus metadatos limpios.
/// </summary>
public record OfertaFeed(
    string Titulo,
    string Enlace,
    string EnlaceDirecto,
    decimal? Precio,
    string? Tienda,
    bool EstaExpirado
);

public interface IChollometroService
{
    /// <summary>
    /// Descarga y parsea el feed oficial de Chollometro en segundo plano de forma asíncrona y resiliente.
    /// </summary>
    Task<List<OfertaFeed>> ObtenerOfertasAsync(CancellationToken ct);

    /// <summary>
    /// Evalúa si el título cumple la regla lógica (+, |, -, paréntesis, sin tildes).
    /// </summary>
    bool CumplePatron(string titulo, string patron);

    /// <summary>
    /// Extrae el precio en euros del texto mediante expresiones regulares.
    /// </summary>
    decimal? ExtraerPrecio(string texto);

    /// <summary>
    /// Identifica comercios conocidos (Amazon, MediaMarkt, El Corte Inglés, etc.) a partir del título, descripción o URL.
    /// </summary>
    string? ExtraerTienda(string titulo, string? summary, string? enlace);

    /// <summary>
    /// Extrae la URL de salida externa directa o la ruta de salto oficial desde el contenido del feed.
    /// </summary>
    string ExtraerEnlaceDirecto(string enlaceChollometro, string? summary);
}