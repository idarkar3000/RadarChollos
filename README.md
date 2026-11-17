# RadarChollos

Bot de Telegram en .NET que revisa el feed de Chollometro cada minuto y avisa cuando aparece una oferta que coincide con alguna de las alertas configuradas.

Lo hice porque perseguir una oferta concreta a mano es agotador: entras, miras, no está, vuelves en dos horas. El bot hace ese trabajo y solo interrumpe cuando hay algo que merece la pena.

## Cómo funciona

1. `RadarWorker` consulta el RSS de Chollometro cada minuto.
2. Cada oferta se compara con las alertas guardadas: patrón de texto, precio máximo y tienda.
3. Las ofertas caducadas, agotadas o marcadas como finalizadas se descartan antes de notificar.
4. Si la oferta es nueva y encaja, se guarda en SQLite y se envía a Telegram con un botón que lleva a la tienda.

Ese último paso tiene su miga. Los enlaces del feed pasan por una redirección de Chollometro, así que el servicio la resuelve por separado con un `HttpClient` que no sigue redirecciones, para quedarse con la URL real de la tienda en lugar de un salto intermedio.

## El motor de alertas

Cada alerta es un patrón con su propia sintaxis, y se escribe directamente en el chat:

| Símbolo | Qué hace | Ejemplo |
|---|---|---|
| espacio | todos los términos deben aparecer | `/add rtx 4070` |
| `+` | obliga a que estén todos | `/add rtx+4070` |
| `\|` | basta con que aparezca uno | `/add rtx\|radeon` |
| `-` | excluye el término | `/add portatil -reacondicionado` |
| `<` | precio máximo | `/add rtx 4070 < 550` |
| `@` | limita a una tienda | `/add rtx 4070 @ amazon` |
| `( )` | agrupa combinaciones | `/add (rtx\|radeon) 4070` |

Un ejemplo completo:

```
/add rtx 4070 @ amazon < 550
```

La comparación normaliza tildes y mayúsculas, así que `camara` encuentra `cámara` y no hace falta escribir el título de la oferta tal cual.

## Stack

| Pieza | Qué uso |
|---|---|
| Lenguaje | C# / .NET 9 |
| Bot | Telegram.Bot |
| Feed | RSS de Chollometro con `System.ServiceModel.Syndication` |
| Persistencia | SQLite con Entity Framework Core |
| Logging | Serilog (consola y fichero con rotación diaria) |
| Despliegue | Docker, con `Dockerfile` y `docker-compose.yml` |

## Estructura

```
RadarChollos/            proyecto principal (worker de fondo + bot)
  Endpoints/             rutas HTTP: /health
  Models/                Chollo y Producto
  Repositories/          AppDbContext, el mapeo de EF Core
  Services/              parsing del feed y manejo de comandos
  Workers/               RadarWorker, el proceso en segundo plano
tests/RadarChollos.Tests/  tests con xUnit
```

## Ejecutar en local

Necesitas el SDK de .NET 9 y un token de bot de BotFather.

```bash
git clone https://github.com/idarkar3000/RadarChollos.git
cd RadarChollos
cp .env.example .env
# rellena TELEGRAM_BOT_TOKEN y TELEGRAM_ALLOWED_CHAT_ID

dotnet run --project RadarChollos
dotnet test
```

El chat id se saca hablándole a `@userinfobot`. El bot solo responde al chat autorizado, así que si no contesta no es un fallo: es que el id no coincide.

## Docker

```bash
docker compose up --build -d
```

El `docker-compose.yml` monta `db/` y `logs/` como volúmenes, de modo que el historial de ofertas y los logs sobreviven a un reinicio del contenedor.

## Configuración

| Variable | Para qué sirve |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Token del bot. Obligatorio, el arranque falla sin él. |
| `TELEGRAM_ALLOWED_CHAT_ID` | Único chat al que responde. |
| `PORT` | Puerto HTTP. Por defecto 10000. |

## Comandos de Telegram

| Comando | Qué hace |
|---|---|
| `/start`, `/help` | Instrucciones de uso. |
| `/add <patrón>` | Crea una alerta. Admite precio máximo y tienda. |
| `/list` | Lista las alertas activas con su id. |
| `/delete <id>` | Elimina una alerta por su id. |

Los comandos se procesan en caliente: no hace falta reiniciar el servicio para cambiar las alertas.

## Decisiones de diseño

**El índice único sobre el enlace está en la base de datos, no en memoria.** Si el proceso se reinicia o se despliega una versión nueva, no vuelve a avisar de ofertas que ya envió. Un `HashSet` en memoria habría perdido ese estado en cada reinicio.

**Borrar una alerta no borra su historial.** La relación entre `Chollo` y `Producto` usa `DeleteBehavior.SetNull`, así que las ofertas ya notificadas se quedan con la regla a null en lugar de desaparecer. Sirve para no repetir avisos si vuelves a crear la misma alerta.

**SQLite en lugar de un motor cliente-servidor.** El volumen es de decenas de ofertas al día y el despliegue es un contenedor único con un volumen montado. Cualquier otra cosa sería infraestructura de más.

## Estado

El bot **no está corriendo ahora mismo**. Estuvo desplegado en Render y se detuvo para no consumir la cuota gratuita del plan. Para levantarlo: `docker compose up --build -d`, o volver a desplegar el `Dockerfile`.

## Limitaciones

- **El esquema se crea con `EnsureCreated`, no con migraciones.** Un cambio en los modelos obliga a borrar `db/chollos.db` y perder el historial.
- **El parsing depende del feed de Chollometro.** Si cambian la estructura del RSS o el formato de los enlaces, esa parte deja de funcionar sin avisar.
- **Un solo chat autorizado.** No hay gestión multiusuario ni varias conversaciones.
- **Sin CI.** Los tests se lanzan a mano con `dotnet test`.

## Licencia

MIT. Mira [LICENSE](LICENSE).
