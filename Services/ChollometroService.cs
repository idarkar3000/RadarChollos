using System.Globalization;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace RadarChollos.Services;

public class ChollometroService : IChollometroService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChollometroService> _logger;

    private static readonly Dictionary<string, string[]> TiendasCatalogo = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Amazon", new[] { "amazon", "amzn" } },
        { "MediaMarkt", new[] { "mediamarkt", "media markt" } },
        { "Carrefour", new[] { "carrefour" } },
        { "El Corte Inglés", new[] { "el corte ingles", "elcorteingles", "eci" } },
        { "Fnac", new[] { "fnac" } },
        { "PcComponentes", new[] { "pccomponentes", "pc componentes" } },
        { "AliExpress", new[] { "aliexpress", "ali express" } },
        { "Miravia", new[] { "miravia" } },
        { "Worten", new[] { "worten" } },
        { "Decathlon", new[] { "decathlon" } },
        { "Leroy Merlin", new[] { "leroy merlin", "leroymerlin" } },
        { "Game", new[] { "game.es", "tiendas game" } },
        { "Tradeinn", new[] { "xtremeinn", "tradeinn", "techinn" } },
        { "Bandai Store", new[] { "bandai namco", "bandainamco" } }
    };

    public ChollometroService(IHttpClientFactory httpClientFactory, ILogger<ChollometroService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<OfertaFeed>> ObtenerOfertasAsync(CancellationToken ct)
    {
        const string url = "https://www.chollometro.com/rss";
        var ofertas = new List<OfertaFeed>();

        try
        {
            var client = _httpClientFactory.CreateClient("ChollometroClient");
            using var stream = await client.GetStreamAsync(url, ct);
            using var xmlReader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(xmlReader);

            foreach (var item in feed.Items)
            {
                string titulo = item.Title?.Text ?? string.Empty;
                string? enlace = item.Links.FirstOrDefault()?.Uri.ToString();

                if (string.IsNullOrEmpty(enlace)) continue;

                bool expirado = EsExpirado(item, enlace);

                decimal? precio = ExtraerPrecio(titulo)
                    ?? (item.Summary != null ? ExtraerPrecio(item.Summary.Text) : null);

                string? merchantTag = ExtraerTagMerchant(item);
                string? tienda = merchantTag != null
                    ? NormalizarNombreTienda(merchantTag)
                    : ExtraerTienda(titulo, item.Summary?.Text, enlace, item);

                string enlaceDirecto = ExtraerEnlaceDirecto(enlace, item.Summary?.Text);

                ofertas.Add(new OfertaFeed(titulo, enlace, enlaceDirecto, precio, tienda, expirado));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al consultar el feed oficial de Chollometro en {Url}", url);
        }

        return ofertas;
    }

    public string ExtraerEnlaceDirecto(string enlaceChollometro, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary))
        {
            var matches = Regex.Matches(summary, @"href=[""'](https?://[^""']+)[""']", RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                string url = m.Groups[1].Value;

                if (!url.Contains("chollometro.com/usuario") &&
                    !url.Contains("chollometro.com/assets") &&
                    !url.Contains("chollometro.com/misc"))
                {
                    if (!url.Contains("chollometro.com") || url.Contains("/visit/"))
                    {
                        return System.Net.WebUtility.HtmlDecode(url);
                    }
                }
            }
        }

        var matchId = Regex.Match(enlaceChollometro, @"-(\d+)$");
        if (matchId.Success)
        {
            return $"https://www.chollometro.com/visit/thread/{matchId.Groups[1].Value}";
        }

        return enlaceChollometro;
    }

    private static bool EsExpirado(SyndicationItem item, string enlace)
    {
        if (enlace.Contains("/expired/", StringComparison.OrdinalIgnoreCase)) return true;

        if (item.Title != null && (
            item.Title.Text.Contains("expirado", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Text.Contains("[finalizado]", StringComparison.OrdinalIgnoreCase) ||
            item.Title.Text.Contains("[agotado]", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (item.Categories.Any(c =>
            c.Name.Contains("expirado", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("finalizado", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("agotado", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (item.Summary != null)
        {
            string s = item.Summary.Text;
            if (s.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("chollo expirado", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("oferta finalizada", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var ext in item.ElementExtensions)
        {
            if (ext.OuterName.Equals("status", StringComparison.OrdinalIgnoreCase) ||
                ext.OuterName.Equals("expired", StringComparison.OrdinalIgnoreCase))
            {
                var val = ext.GetObject<XElement>().Value;
                if (val.Equals("expired", StringComparison.OrdinalIgnoreCase) || val.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ExtraerTagMerchant(SyndicationItem item)
    {
        try
        {
            foreach (var ext in item.ElementExtensions)
            {
                if (ext.OuterName.Equals("merchant", StringComparison.OrdinalIgnoreCase))
                {
                    var xElem = ext.GetObject<XElement>();
                    string? nombre = xElem.Attribute("name")?.Value ?? xElem.Value;
                    if (!string.IsNullOrWhiteSpace(nombre)) return nombre;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public string? ExtraerTienda(string titulo, string? summary, string? enlace) =>
        ExtraerTienda(titulo, summary, enlace, null);

    private string? ExtraerTienda(string titulo, string? summary, string? enlace, SyndicationItem? item)
    {
        var enlacesExtra = item?.Links.Select(l => l.Uri.ToString()) ?? Enumerable.Empty<string>();
        string categorias = item != null ? string.Join(" ", item.Categories.Select(c => c.Name)) : string.Empty;

        string textoCompleto = $"{titulo} {summary ?? string.Empty} {string.Join(" ", enlacesExtra)} {categorias} {enlace ?? string.Empty}";

        foreach (var (nombreTienda, alias) in TiendasCatalogo)
        {
            foreach (var a in alias)
            {
                if (Regex.IsMatch(textoCompleto, $@"(?:\b|_){Regex.Escape(a)}(?:\b|_)", RegexOptions.IgnoreCase))
                {
                    return nombreTienda;
                }
            }
        }

        return null;
    }

    private static string NormalizarNombreTienda(string nombreOriginal)
    {
        foreach (var (nombreTienda, alias) in TiendasCatalogo)
        {
            if (nombreOriginal.Contains(nombreTienda, StringComparison.OrdinalIgnoreCase) ||
                alias.Any(a => nombreOriginal.Contains(a, StringComparison.OrdinalIgnoreCase)))
            {
                return nombreTienda;
            }
        }

        return nombreOriginal.Trim();
    }

    public bool CumplePatron(string titulo, string patron)
    {
        string tituloLimpio = QuitarTildes(titulo);

        var partesMenos = patron.Split('-', StringSplitOptions.TrimEntries);
        string partePositiva = partesMenos[0];

        for (int i = 1; i < partesMenos.Length; i++)
        {
            string palabraExcluida = QuitarTildes(partesMenos[i]);
            if (!string.IsNullOrWhiteSpace(palabraExcluida) &&
                tituloLimpio.Contains(palabraExcluida, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        int inicioParentesis = partePositiva.IndexOf('(');
        int finParentesis = partePositiva.IndexOf(')');

        if (inicioParentesis != -1 && finParentesis > inicioParentesis)
        {
            string parteObligatoria = partePositiva.Remove(inicioParentesis, finParentesis - inicioParentesis + 1);
            var terminosObligatorios = parteObligatoria.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!terminosObligatorios.All(t => tituloLimpio.Contains(QuitarTildes(t), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string contenidoParentesis = partePositiva.Substring(inicioParentesis + 1, finParentesis - inicioParentesis - 1);
            var alternativas = contenidoParentesis.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return alternativas.Any(alt => tituloLimpio.Contains(QuitarTildes(alt), StringComparison.OrdinalIgnoreCase));
        }

        var opcionesOr = partePositiva.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return opcionesOr.Any(opcion =>
        {
            var terminosAnd = opcion.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return terminosAnd.All(termino =>
                tituloLimpio.Contains(QuitarTildes(termino), StringComparison.OrdinalIgnoreCase));
        });
    }

    public decimal? ExtraerPrecio(string texto)
    {
        var match = Regex.Match(texto, @"(\d+([.,]\d{1,2})?)\s*(?:€|\?|euros?)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        string valorLimpio = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(valorLimpio, CultureInfo.InvariantCulture, out decimal precio) ? precio : null;
    }

    private static string QuitarTildes(string texto)
    {
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}