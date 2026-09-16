using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadarChollos.Data;
using RadarChollos.Models;
using RadarChollos.Services;
using RadarChollos.Workers;
using Serilog;
using Serilog.Events;
using Telegram.Bot;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/radar-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Arrancando RadarChollos...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddSerilog();

    Directory.CreateDirectory("db");
    Directory.CreateDirectory("logs");

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite("Data Source=db/chollos.db"));

    builder.Services.AddHttpClient("ChollometroClient", client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);
    });

    builder.Services.AddHttpClient("RedirectResolverClient", client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(10);
    }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

    string botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
        ?? builder.Configuration["TelegramBotToken"]
        ?? throw new InvalidOperationException("Falta la variable de entorno TELEGRAM_BOT_TOKEN.");

    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(botToken));
    builder.Services.AddSingleton<IChollometroService, ChollometroService>();
    builder.Services.AddSingleton<ITelegramHandlerService, TelegramHandlerService>();
    builder.Services.AddHostedService<RadarWorker>();

    // Render asigna el puerto mediante la variable de entorno PORT (por defecto 10000)
    string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    var app = builder.Build();

    // Endpoint de comprobación de estado para Render y el monitor de pings
    app.MapGet("/", () => Results.Ok(new { status = "healthy", app = "RadarChollos" }));

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Productos.AnyAsync())
        {
            db.Productos.AddRange(
                new Producto { Patron = "ps5 - digital - funda", PrecioMaximo = 450m },
                new Producto { Patron = "taladro + bosch", PrecioMaximo = 80m },
                new Producto { Patron = "minecraft + switch", PrecioMaximo = 25m, TiendaFiltro = "amazon" }
            );
            await db.SaveChangesAsync();
            Log.Information("Base de datos lista: añadidas 3 reglas iniciales.");
        }
    }

    Log.Information("Servicio en ejecucion. Presiona Ctrl+C para detener.");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Error critico al iniciar el host.");
}
finally
{
    Log.CloseAndFlush();
}