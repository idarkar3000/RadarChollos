# RadarChollos

Bot de Telegram en .NET que revisa el feed de Chollometro cada minuto y avisa cuando aparece una oferta que coincide con alguna de las alertas configuradas.

## Estructura del Repositorio

- `RadarChollos/`: Proyecto principal en .NET (worker de fondo + bot de Telegram).
- `tests/RadarChollos.Tests/`: Pruebas unitarias con xUnit.

## Tecnologías

- **Lenguaje:** C# (.NET 9)
- **Base de datos:** SQLite con Entity Framework Core
- **Mensajería:** Telegram.Bot
- **Feed:** RSS de Chollometro (System.ServiceModel.Syndication)
- **Logging:** Serilog
- **Despliegue:** Docker en Render, con UptimeRobot para evitar la suspensión por inactividad

## Estructura del Proyecto

- `Endpoints/`: Rutas HTTP del servicio (`HealthEndpoints`).
- `Models/`: Entidades de dominio (`Chollo`, `Producto`).
- `Repositories/`: Capa de datos con EF Core (`AppDbContext`).
- `Services/`: Lógica de parsing y del bot (`ChollometroService`, `TelegramHandlerService`).
- `Workers/`: Proceso en segundo plano (`RadarWorker`).

## Cómo funciona

1. `RadarWorker` consulta el RSS de Chollometro cada minuto.
2. Cada oferta se compara con las alertas guardadas (patrón, precio máximo y tienda).
3. Si es nueva y coincide, se guarda en la base de datos y se envía un mensaje a Telegram con botón directo a la tienda.

## Comandos de Telegram

Los comandos se procesan en caliente, sin reiniciar el servicio:

- `/start` o `/help`: Muestra las instrucciones de uso.
- `/list`: Lista las alertas activas con su `ID`.
- `/add`: Crea una nueva alerta. Ejemplo: `/add rtx 4070 @ amazon < 550`
  - `+` obliga a que estén todos los términos, `|` basta con uno, `-` excluye.
  - `<` marca el precio máximo y `@` la tienda.
- `/delete`: Elimina una alerta por su ID. Ejemplo: `/delete 3`

Solo responde al chat configurado en `TELEGRAM_ALLOWED_CHAT_ID`.

## Pruebas

```bash
dotnet test
```

Están en `tests/RadarChollos.Tests` y cubren el motor de coincidencia de patrones (`MatchingRuleTests`) y el parser del comando `/add` (`CommandParserTests`).

## Configuración y Ejecución

### Variables de entorno

Copia `.env.example` a `.env` en la raíz del repositorio:

```
TELEGRAM_BOT_TOKEN=tu_token_aqui
TELEGRAM_ALLOWED_CHAT_ID=tu_chat_id_aqui
```

El token lo da BotFather y el chat id se puede obtener hablándole al bot `@userinfobot`.

### Comandos

```bash
dotnet restore
dotnet build
dotnet run --project RadarChollos
dotnet test
docker compose up --build -d
```

El servicio escucha en el puerto `10000` (o el que indique la variable `PORT`) y responde en `/health` para las comprobaciones de Render y UptimeRobot.
