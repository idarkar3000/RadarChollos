namespace RadarChollos.Services
{
    // Oferta leída del feed RSS de Chollometro ya limpia
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
        Task<List<OfertaFeed>> ObtenerOfertasAsync(CancellationToken ct);

        // Evalúa el patrón lógico (+, |, -, paréntesis) contra el título de la oferta
        bool CumplePatron(string titulo, string patron);

        decimal? ExtraerPrecio(string texto);

        string? ExtraerTienda(string titulo, string? summary, string? enlace);

        string ExtraerEnlaceDirecto(string enlaceChollometro, string? summary);
    }
}
