using Telegram.Bot.Types;

namespace RadarChollos.Services
{
    public interface ITelegramHandlerService
    {
        Task EnviarAlertaCholloAsync(
            string titulo,
            decimal? precio,
            string? tienda,
            string enlace,
            decimal? precioAnterior,
            CancellationToken ct);

        Task HandleUpdateAsync(Update update, CancellationToken ct);
    }
}
