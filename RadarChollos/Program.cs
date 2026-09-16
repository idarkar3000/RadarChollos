using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadarChollos.Data;
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

    string port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    var app = builder.Build();

    // Permite peticiones GET y HEAD en ambos endpoints para UptimeRobot y Render
    app.MapMethods("/", ["GET", "HEAD"], () => Results.Ok(new { status = "healthy", app = "RadarChollos" }));
    app.MapMethods("/health", ["GET", "HEAD"], () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        Log.Information("Base de datos verificada y lista para su uso.");
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