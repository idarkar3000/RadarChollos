# RadarChollos 📡🏷️

RadarChollos es un servicio backend desarrollado en **C# sobre .NET 9** enfocado en la automatización del rastreo de ofertas y la notificación inmediata mediante Telegram. El sistema resuelve el problema del seguimiento manual de productos consumiendo periódicamente las publicaciones de Chollometro, evaluando criterios de filtrado personalizados y enviando alertas directas a un usuario específico.

La aplicación opera mediante un proceso de fondo continuo implementado con `IHostedService`, complementado por un servidor web ligero construido con el SDK de ASP.NET Core. Esta combinación permite que el motor de búsqueda trabaje de forma ininterrumpida mientras expone una ruta HTTP básica que responde con el estado de salud del sistema, requisito indispensable para su despliegue y validación en plataformas cloud.

El filtrado de datos se gestiona a través de un motor interno de reglas almacenadas en una base de datos SQLite mediante Entity Framework Core. Cada regla permite definir palabras clave obligatorias, términos excluidos, límites de precio y normalización de comercios habituales como Amazon, PcComponentes o MediaMarkt. Durante cada ciclo de ejecución, el servicio descarga el feed, descarta ofertas duplicadas o que no cumplan los criterios técnicos establecidos y genera mensajes interactivos con enlaces directos hacia el comercio final.

Toda la interacción con el bot se realiza desde el propio chat de Telegram. El usuario dispone de comandos para listar los filtros activos, registrar nuevas búsquedas y borrar reglas obsoletas en caliente, sin necesidad de reiniciar el proceso ni alterar archivos de configuración. El comando /start o /help muestra las instrucciones de uso y el formato de los filtros. El comando /list devuelve la lista de reglas almacenadas junto con el identificador único de cada una. El comando /add permite registrar una nueva regla especificando términos clave, exclusiones precedidas por un signo menos, precio máximo opcional y tienda objetivo precedida por una arroba, como por ejemplo /add rtx 4070 -ti 550 @pccomponentes. El comando /delete seguido del número identificador elimina esa regla de la base de datos de manera definitiva. La API de Telegram se maneja de forma asíncrona mediante la librería oficial Telegram.Bot, restringiendo la ejecución de comandos a un identificador de usuario verificado para evitar accesos no autorizados.

Para el despliegue se utiliza una compilación multi-etapa con Docker. En la primera fase se emplea la imagen del SDK para restaurar paquetes NuGet y compilar los binarios del proyecto. En la fase final se trasladan únicamente los archivos compilados a una imagen base de ASP.NET Core en Linux, reduciendo el tamaño total del contenedor y el consumo de recursos en memoria.

El servicio corre alojado en Render bajo una instancia web gratuita. Para evitar la suspensión automática por inactividad tras periodos sin tráfico entrante, el sistema está enlazado a UptimeRobot, que realiza una petición de comprobación cada cinco minutos sobre el puerto expuesto. Las credenciales sensibles, como el token del bot y el identificador de chat permitido, se gestionan íntegramente mediante variables de entorno del sistema, impidiendo la exposición de secretos en el control de versiones de Git.

---

## 🏗️ Arquitectura y Tecnologías

* **C# / .NET 9:** Runtime y lenguaje base del sistema.
* **Worker continuo (`IHostedService`):** Proceso en segundo plano desacoplado para ejecución periódica.
* **Entity Framework Core & SQLite:** Persistencia local para gestión de reglas y deduplicación.
* **Telegram.Bot:** Cliente asíncrono para recepción de comandos y envío de alertas.
* **xUnit & FluentAssertions:** Batería automatizada de pruebas unitarias.
* **Docker & Render:** Contenerización multi-stage y alojamiento en la nube con keep-alive vía UptimeRobot.

---

## 💬 Comandos de Telegram

El bot procesa los comandos en caliente sin reiniciar el servicio:

* `/start` o `/help`: Muestra las instrucciones de uso y el formato de los filtros.
* `/list`: Devuelve la lista de reglas activas junto con el identificador único (`ID`) de cada una.
* `/add`: Registra una nueva regla indicando palabras clave, términos excluidos con `-`, precio máximo con `<` y tienda con `@`. Ejemplo: `/add rtx 4070 -ti 550 <800 @pccomponentes`
* `/delete`: Elimina de forma definitiva la regla asociada al número identificador. Ejemplo: `/delete 3`

---

## 🧪 Pruebas Unitarias

La suite de pruebas en el proyecto `RadarChollos.Tests` cubre los dos componentes críticos del bot:

* Comando de ejecución en terminal: `dotnet test`
* **Matching Engine (`MatchingRuleTests.cs`):** Valida la inclusión de palabras clave, el descarte por términos excluidos (`-`), los topes de precio y la normalización de comercios (`@`).
* **Command Parser (`CommandParserTests.cs`):** Valida la interpretación sintáctica de `/add` y `/delete`, los parámetros opcionales y la gestión de entradas inválidas.

---

## 🚀 Puesta en Marcha Local y Despliegue

### Variables de entorno (.env)

Crea un archivo llamado `.env` en la raíz tomando como referencia `.env.example` con los siguientes parámetros:

* `TELEGRAM_BOT_TOKEN`: Token obtenido a través de BotFather.
* `ALLOWED_CHAT_ID`: Identificador numérico de tu usuario de Telegram.
* `CHECK_INTERVAL_SECONDS`: Frecuencia de escaneo en segundos (por defecto 60).

### Comandos de ejecución

* Restaurar dependencias: `dotnet restore`
* Compilar solución: `dotnet build`
* Ejecutar bot en local: `dotnet run --project RadarChollos`
* Ejecutar tests unitarios: `dotnet test`
* Levantar mediante Docker Compose: `docker compose up --build -d`
