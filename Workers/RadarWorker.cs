using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadarChollos.Data;
using RadarChollos.Models;
using RadarChollos.Services;
using Telegram.Bot;

namespace RadarChollos.Workers;

public class RadarWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChollometroService _chollometroService;
    private readonly ITelegramHandlerService _telegramHandler;
    private readonly ITelegramBotClient _botClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RadarWorker> _logger;

    private DateTime _ultimaLimpiezaDb = DateTime.MinValue;

    public RadarWorker(
        IServiceScopeFactory scopeFactory,
        IChollometroService chollometroService,
        ITelegramHandlerService telegramHandler,
        ITelegramBotClient botClient,
        IHttpClientFactory httpClientFactory,
        ILogger<RadarWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _chollometroService = chollometroService;
        _telegramHandler = telegramHandler;
        _botClient = botClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RadarChollos Worker iniciado correctamente.");

        _botClient.StartReceiving(
            updateHandler: (cli, update, ct) => _telegramHandler.HandleUpdateAsync(update, ct),
            errorHandler: (cli, ex, ct) =>
            {
                _logger.LogError(ex, "Error recibido desde la API de Telegram.");
                return Task.CompletedTask;
            },
            cancellationToken: stoppingToken
        );

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcesarRondaAsync(stoppingToken);
                await LimpiarHistoricoAntiguoAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error no controlado durante la ronda de monitorización.");
            }
        }

        _logger.LogInformation("RadarChollos Worker detenido de forma segura (Graceful Shutdown).");
    }

    private async Task ProcesarRondaAsync(CancellationToken ct)
    {
        _logger.LogInformation("Iniciando ronda de sondeo del feed oficial...");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reglas = await db.Productos.ToListAsync(ct);
        if (!reglas.Any())
        {
            _logger.LogDebug("No hay reglas activas configuradas.");
            return;
        }

        var todasLasOfertas = await _chollometroService.ObtenerOfertasAsync(ct);
        if (!todasLasOfertas.Any())
        {
            _logger.LogWarning("No se recibieron ofertas del feed en esta pasada.");
            return;
        }

        foreach (var regla in reglas)
        {
            var ofertasCoincidentes = todasLasOfertas
                .Where(o => !o.EstaExpirado && _chollometroService.CumplePatron(o.Titulo, regla.Patron) && o.Precio.HasValue)
                .ToList();

            if (!string.IsNullOrEmpty(regla.TiendaFiltro))
            {
                ofertasCoincidentes = ofertasCoincidentes
                    .Where(o => o.Tienda != null && o.Tienda.Contains(regla.TiendaFiltro, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!ofertasCoincidentes.Any()) continue;

            var enlacesVivos = ofertasCoincidentes.Select(o => o.Enlace).ToHashSet();
            var chollosGuardadosVivos = await db.Chollos
                .Where(c => c.ProductoId == regla.Id && c.Precio.HasValue && enlacesVivos.Contains(c.Enlace))
                .ToListAsync(ct);

            decimal? sueloDinamico = chollosGuardadosVivos.Any()
                ? chollosGuardadosVivos.Min(c => c.Precio!.Value)
                : regla.PrecioMaximo;

            var candidatos = new List<OfertaFeed>();
            foreach (var oferta in ofertasCoincidentes)
            {
                string enlaceLimpio = oferta.Enlace.Split('?')[0];
                string tituloLimpio = oferta.Titulo.Trim().ToLowerInvariant();

                bool yaNotificado = await db.Chollos.AnyAsync(c =>
                    c.Enlace.StartsWith(enlaceLimpio) ||
                    (c.Titulo.ToLower() == tituloLimpio && c.FechaDeteccion >= DateTime.UtcNow.AddHours(-24)),
                    ct);

                if (yaNotificado) continue;

                if (sueloDinamico.HasValue)
                {
                    if (oferta.Precio!.Value <= sueloDinamico.Value)
                    {
                        candidatos.Add(oferta);
                    }
                    else
                    {
                        _logger.LogDebug("Descartado por precio: {Titulo} ({Precio}€ > {Suelo}€)",
                            oferta.Titulo, oferta.Precio, sueloDinamico);
                    }
                }
                else
                {
                    candidatos.Add(oferta);
                }
            }

            var mejorCandidato = candidatos.OrderBy(c => c.Precio).FirstOrDefault();

            if (mejorCandidato != null)
            {
                _logger.LogInformation("¡Chollo detectado para '{Patron}'! {Titulo} - {Precio}€ en {Tienda}",
                    regla.Patron, mejorCandidato.Titulo, mejorCandidato.Precio, mejorCandidato.Tienda ?? "Desconocida");

                string urlFinal = await ResolverRedireccionFinalAsync(mejorCandidato.EnlaceDirecto, ct);

                await _telegramHandler.EnviarAlertaCholloAsync(
                    mejorCandidato.Titulo,
                    mejorCandidato.Precio,
                    mejorCandidato.Tienda,
                    urlFinal,
                    sueloDinamico,
                    ct
                );

                db.Chollos.Add(new Chollo
                {
                    Titulo = mejorCandidato.Titulo,
                    Enlace = mejorCandidato.Enlace,
                    Precio = mejorCandidato.Precio,
                    Tienda = mejorCandidato.Tienda,
                    ProductoId = regla.Id,
                    FechaDeteccion = DateTime.UtcNow
                });

                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<string> ResolverRedireccionFinalAsync(string urlOriginal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(urlOriginal) || !urlOriginal.Contains("chollometro.com"))
        {
            return urlOriginal;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("RedirectResolverClient");
            using var request = new HttpRequestMessage(HttpMethod.Get, urlOriginal);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.Headers.Location != null)
            {
                return response.Headers.Location.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo resolver redirección para {Url}. Usando URL de salto.", urlOriginal);
        }

        return urlOriginal;
    }

    private async Task LimpiarHistoricoAntiguoAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _ultimaLimpiezaDb < TimeSpan.FromHours(24)) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var fechaLimite = DateTime.UtcNow.AddDays(-30);
            int borrados = await db.Chollos
                .Where(c => c.FechaDeteccion < fechaLimite)
                .ExecuteDeleteAsync(ct);

            if (borrados > 0)
            {
                _logger.LogInformation("Mantenimiento: Se eliminaron {Cantidad} chollos antiguos de la base de datos.", borrados);
            }

            _ultimaLimpiezaDb = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error durante el mantenimiento de purga en la base de datos.");
        }
    }
}