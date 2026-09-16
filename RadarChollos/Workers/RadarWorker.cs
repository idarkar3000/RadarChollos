using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadarChollos.Data;
using RadarChollos.Models;
using RadarChollos.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace RadarChollos.Workers;

public class RadarWorker : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ITelegramHandlerService _telegramHandler;
    private readonly IChollometroService _chollometroService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RadarWorker> _logger;

    public RadarWorker(
        ITelegramBotClient botClient,
        ITelegramHandlerService telegramHandler,
        IChollometroService chollometroService,
        IServiceScopeFactory scopeFactory,
        ILogger<RadarWorker> logger)
    {
        _botClient = botClient;
        _telegramHandler = telegramHandler;
        _chollometroService = chollometroService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(() => IniciarListenerTelegramConReintentoAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarRondaFeedAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Ronda] Error evaluando feed de ofertas");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task IniciarListenerTelegramConReintentoAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("[Telegram] Listener activo y escuchando eventos");

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = []
                };

                await _botClient.ReceiveAsync(
                    updateHandler: ManejarUpdateAsync,
                    errorHandler: ManejarErrorPollingAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Telegram] Conexión interrumpida. Reconectando en 10 segundos...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private Task ManejarErrorPollingAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        _logger.LogWarning("[Telegram API Error] {Mensaje}", exception.Message);
        return Task.CompletedTask;
    }

    private async Task ManejarUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            await _telegramHandler.HandleUpdateAsync(update, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Error procesando comando");
        }
    }

    private async Task ProcesarRondaFeedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var productos = await db.Productos.ToListAsync(ct);
        if (productos.Count == 0)
        {
            _logger.LogInformation("[Ronda] Sin alertas activas registradas.");
            return;
        }

        var ofertas = await _chollometroService.ObtenerOfertasAsync(ct);
        int notificados = 0;

        foreach (var oferta in ofertas)
        {
            if (oferta.EstaExpirado || string.IsNullOrWhiteSpace(oferta.Enlace))
                continue;

            bool yaExiste = await db.Chollos.AnyAsync(c => c.Enlace == oferta.Enlace, ct);
            if (yaExiste)
                continue;

            foreach (var producto in productos)
            {
                if (!_chollometroService.CumplePatron(oferta.Titulo, producto.Patron))
                    continue;

                if (producto.PrecioMaximo.HasValue && oferta.Precio.HasValue && oferta.Precio.Value > producto.PrecioMaximo.Value)
                    continue;

                if (!string.IsNullOrWhiteSpace(producto.TiendaFiltro) &&
                    !string.Equals(oferta.Tienda, producto.TiendaFiltro, StringComparison.OrdinalIgnoreCase))
                    continue;

                var nuevoChollo = new Chollo
                {
                    Titulo = oferta.Titulo,
                    Enlace = oferta.Enlace,
                    Precio = oferta.Precio,
                    Tienda = oferta.Tienda,
                    ProductoId = producto.Id,
                    FechaDeteccion = DateTime.UtcNow
                };

                db.Chollos.Add(nuevoChollo);
                await db.SaveChangesAsync(ct);

                await _telegramHandler.EnviarAlertaCholloAsync(
                    oferta.Titulo,
                    oferta.Precio,
                    oferta.Tienda,
                    oferta.EnlaceDirecto,
                    null,
                    ct
                );

                notificados++;
                break;
            }
        }

        _logger.LogInformation("[Ronda] Feed analizado: {Total} ofertas leidas | {Reglas} reglas evaluadas | {Notificados} notificados.",
            ofertas.Count, productos.Count, notificados);
    }
}