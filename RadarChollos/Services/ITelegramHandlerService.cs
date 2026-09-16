using Telegram.Bot.Types;

namespace RadarChollos.Services;

public interface ITelegramHandlerService
{
    /// <summary>
    /// Envía un mensaje con formato Markdown y botón interactivo cuando se detecta un nuevo mínimo.
    /// </summary>
    Task EnviarAlertaCholloAsync(
        string titulo,
        decimal? precio,
        string? tienda,
        string enlace,
        decimal? precioAnterior,
        CancellationToken ct);

    /// <summary>
    /// Procesa de forma segura los comandos entrantes del usuario (/add, /list, /remove).
    /// </summary>
    Task HandleUpdateAsync(Update update, CancellationToken ct);
}