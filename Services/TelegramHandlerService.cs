using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadarChollos.Data;
using RadarChollos.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace RadarChollos.Services;

public class TelegramHandlerService : ITelegramHandlerService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramHandlerService> _logger;
    private readonly long _allowedChatId;

    public TelegramHandlerService(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TelegramHandlerService> logger)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _logger = logger;

        string chatIdRaw = Environment.GetEnvironmentVariable("TELEGRAM_ALLOWED_CHAT_ID")
            ?? configuration["TelegramAllowedChatId"]
            ?? "0";

        long.TryParse(chatIdRaw, out _allowedChatId);
    }

    public async Task EnviarAlertaCholloAsync(
        string titulo,
        decimal? precio,
        string? tienda,
        string enlace,
        decimal? precioAnterior,
        CancellationToken ct)
    {
        string textoPrecio = precio.HasValue ? $"{precio.Value:0.00}€" : "N/D";
        string textoTienda = !string.IsNullOrWhiteSpace(tienda) ? tienda : "Chollometro";

        string detalleBajada = (precioAnterior.HasValue && precio.HasValue && precio.Value < precioAnterior.Value)
            ? $"\n📉 <b>Precio minimo superado</b> (Anterior: <s>{precioAnterior.Value:0.00}€</s>)"
            : "";

        var sb = new StringBuilder();
        sb.AppendLine("🚨 <b>Alerta de Chollo</b>");
        if (!string.IsNullOrEmpty(detalleBajada)) sb.AppendLine(detalleBajada);
        sb.AppendLine();
        sb.AppendLine($"📦 <b>Producto:</b> {System.Net.WebUtility.HtmlEncode(titulo)}");
        sb.AppendLine($"🏪 <b>Tienda:</b> <code>{System.Net.WebUtility.HtmlEncode(textoTienda)}</code>");
        sb.AppendLine($"💰 <b>Precio:</b> <b>{textoPrecio}</b>");
        sb.AppendLine();
        sb.AppendLine($"🔗 <a href=\"{enlace}\">Enlace a la oferta</a>");

        string etiquetaBoton = !string.IsNullOrWhiteSpace(tienda) && !tienda.Contains("Chollometro", StringComparison.OrdinalIgnoreCase)
            ? $"Ver en {tienda}"
            : "Ver oferta";

        var teclado = new InlineKeyboardMarkup(new List<List<InlineKeyboardButton>>
        {
            new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithUrl(etiquetaBoton, enlace)
            }
        });

        try
        {
            await _bot.SendMessage(
                chatId: _allowedChatId,
                text: sb.ToString(),
                parseMode: ParseMode.Html,
                replyMarkup: teclado,
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al enviar mensaje a Telegram.");
        }
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } mensajeTexto) return;
        long remitenteId = update.Message.Chat.Id;

        if (_allowedChatId != 0 && remitenteId != _allowedChatId)
        {
            _logger.LogWarning("Acceso rechazado para ChatId no autorizado: {ChatId}", remitenteId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (mensajeTexto.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
            mensajeTexto.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            await EnviarAyudaAsync(remitenteId, ct);
            return;
        }

        if (mensajeTexto.Equals("/add", StringComparison.OrdinalIgnoreCase) ||
            mensajeTexto.StartsWith("/add ", StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarAddAsync(mensajeTexto, remitenteId, db, ct);
            return;
        }

        if (mensajeTexto.StartsWith("/remove ", StringComparison.OrdinalIgnoreCase) ||
            mensajeTexto.StartsWith("/del ", StringComparison.OrdinalIgnoreCase) ||
            mensajeTexto.Equals("/remove", StringComparison.OrdinalIgnoreCase) ||
            mensajeTexto.Equals("/del", StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarRemoveAsync(mensajeTexto, remitenteId, db, ct);
            return;
        }

        if (mensajeTexto.Equals("/list", StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarListAsync(remitenteId, db, ct);
            return;
        }
    }

    private async Task EnviarAyudaAsync(long chatId, CancellationToken ct)
    {
        string ayuda = "<b>Comandos disponibles:</b>\n\n" +
                       "• <code>/list</code> : Lista las alertas activas.\n" +
                       "• <code>/remove &lt;id&gt;</code> : Elimina una alerta por ID.\n" +
                       "• <code>/add &lt;patron&gt;</code> : Registra un nuevo patron.\n\n" +
                       "<b>Formatos admitidos:</b>\n" +
                       "• <code>/add ps5 &lt; 450</code>\n" +
                       "• <code>/add switch + zelda &lt; 45</code>\n" +
                       "• <code>/add monitor + msi - curvo &lt; 180</code>\n" +
                       "• <code>/add rtx 4070 @ amazon &lt; 550</code>";

        await _bot.SendMessage(chatId: chatId, text: ayuda, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task ProcesarAddAsync(string texto, long chatId, AppDbContext db, CancellationToken ct)
    {
        string entrada = texto.Length > 4 ? texto[4..].Trim() : "";

        if (string.IsNullOrWhiteSpace(entrada) || entrada.Length < 3)
        {
            await _bot.SendMessage(chatId: chatId, text: "Uso: <code>/add &lt;patron&gt; [&lt; precio] [@ tienda]</code>", parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        string patronRestante = entrada;
        decimal? precioMax = null;
        string? tiendaFiltro = null;

        if (patronRestante.Contains('<'))
        {
            var partesPrecio = patronRestante.Split('<', StringSplitOptions.TrimEntries);
            patronRestante = partesPrecio[0];

            if (partesPrecio.Length > 1 && decimal.TryParse(partesPrecio[1].Replace(',', '.'), CultureInfo.InvariantCulture, out decimal pParseado))
            {
                precioMax = pParseado;
            }
        }

        if (patronRestante.Contains('@'))
        {
            var partesTienda = patronRestante.Split('@', StringSplitOptions.TrimEntries);
            patronRestante = partesTienda[0];
            if (partesTienda.Length > 1)
            {
                tiendaFiltro = partesTienda[1].ToLowerInvariant();
            }
        }

        var partesMenos = patronRestante.Split('-', StringSplitOptions.TrimEntries);
        string patronPositivo = partesMenos[0];
        var exclusiones = partesMenos.Skip(1).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();

        var nuevoProducto = new Producto
        {
            Patron = patronRestante.Trim(),
            PrecioMaximo = precioMax,
            TiendaFiltro = tiendaFiltro
        };

        db.Productos.Add(nuevoProducto);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[Telegram] Nueva alerta creada: '{Patron}' (Max: {Max}€ | Tienda: {Tienda})",
            nuevoProducto.Patron, precioMax?.ToString() ?? "N/A", tiendaFiltro ?? "Cualquiera");

        var respuesta = new StringBuilder();
        respuesta.AppendLine("<b>Alerta registrada:</b>");
        respuesta.AppendLine($"• <b>Patron:</b> <code>{System.Net.WebUtility.HtmlEncode(patronPositivo)}</code>");

        if (exclusiones.Any())
            respuesta.AppendLine($"• <b>Exclusiones:</b> <code>{System.Net.WebUtility.HtmlEncode(string.Join(", ", exclusiones))}</code>");

        if (!string.IsNullOrEmpty(tiendaFiltro))
            respuesta.AppendLine($"• <b>Tienda:</b> <code>{System.Net.WebUtility.HtmlEncode(tiendaFiltro)}</code>");

        respuesta.AppendLine(precioMax.HasValue
            ? $"• <b>Precio limite:</b> <code>{precioMax.Value:0.00}€</code>"
            : "• <b>Precio limite:</b> <code>Ninguno</code>");

        await _bot.SendMessage(chatId: chatId, text: respuesta.ToString(), parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task ProcesarRemoveAsync(string texto, long chatId, AppDbContext db, CancellationToken ct)
    {
        var partes = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length < 2 || !int.TryParse(partes[1], out int productoId))
        {
            await _bot.SendMessage(chatId: chatId, text: "Uso: <code>/remove &lt;id&gt;</code>", parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        var producto = await db.Productos.FindAsync([productoId], ct);
        if (producto == null)
        {
            await _bot.SendMessage(chatId: chatId, text: $"No se encontro la alerta con ID <code>{productoId}</code>.", parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        db.Productos.Remove(producto);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[Telegram] Alerta eliminada: ID {Id} ('{Patron}')", producto.Id, producto.Patron);

        await _bot.SendMessage(chatId: chatId, text: $"Alerta eliminada: <code>(ID: {producto.Id})</code> {System.Net.WebUtility.HtmlEncode(producto.Patron)}", parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task ProcesarListAsync(long chatId, AppDbContext db, CancellationToken ct)
    {
        var productos = await db.Productos.ToListAsync(ct);
        if (!productos.Any())
        {
            await _bot.SendMessage(chatId: chatId, text: "No hay alertas configuradas.", cancellationToken: ct);
            return;
        }

        var lineas = productos.Select(p =>
        {
            string precio = p.PrecioMaximo.HasValue ? $"&lt;= {p.PrecioMaximo.Value:0.00}€" : "Sin limite";
            string tienda = !string.IsNullOrEmpty(p.TiendaFiltro) ? $" | @{p.TiendaFiltro}" : "";
            return $"• <code>[{p.Id}]</code> <b>{System.Net.WebUtility.HtmlEncode(p.Patron)}</b> ({precio}{tienda})";
        });

        await _bot.SendMessage(
            chatId: chatId,
            text: "<b>Alertas activas:</b>\n\n" + string.Join("\n", lineas),
            parseMode: ParseMode.Html,
            cancellationToken: ct
        );
    }
}