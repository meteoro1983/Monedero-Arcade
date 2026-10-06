# Especificación técnica: Sistema de crédito virtual para PC Arcade (RetroBat)

## 1. Contexto y objetivo

Se está armando una **PC arcade para un bar**, corriendo **RetroBat** (fork de EmulationStation) sobre **Windows 10**, en modo kiosko, con una única lista de favoritos que mezcla juegos de MAME/arcade y juegos de consola/PC emulados.

**Problema a resolver:** los juegos de MAME/arcade respetan naturalmente la lógica de "monedero" (partidas finitas, se pierde y hay que reinsertar crédito). Los juegos de consola o PC no tienen ese límite natural: un cliente puede jugar una partida interminable (ej. un RPG o un Mario Kart) mientras otros clientes del bar esperan su turno sin poder usar la máquina.

**Objetivo del programa:** crear una herramienta que corra en segundo plano en Windows y **le imponga artificialmente la lógica de monedero a juegos que no la tienen de forma nativa**, interrumpiendo la partida con un overlay de "insertar crédito" después de un tiempo configurable, y repitiendo esa interrupción cada cierto intervalo mientras el juego siga activo. El objetivo final es que el cliente deba comprar más crédito o resignar la partida, liberando la máquina para otros — sin importar si juega el jugador 1 o el jugador 2 en modo multiplayer local (no hace falta diferenciarlos).

**Meta adicional:** el programa está pensado para eventualmente publicarse como **proyecto open source**, funcional y terminado, para que otros usuarios de RetroBat con el mismo problema (cabinets de bar/local comercial) puedan usarlo. Esto implica que el diseño debe ser genérico desde el principio (nada hardcodeado a la configuración de un usuario particular), bien documentado, y con una licencia definida (a decidir; MIT es la sugerencia por defecto por simplicidad).

**Prioridad de desarrollo:** lo indispensable es que el programa sea **eficiente, simple y estable** — que no genere errores ni conflictos al superponerse con el emulador o con RetroBat, y que cumpla su función básica (marcar que hay que insertar crédito, contar, pausar, y salir del emulador si se agota el tiempo) de forma discreta y confiable. El texto en pantalla puede ser simple y de estética retro básica, sin necesidad de ser sofisticado. Todo lo relacionado a estética avanzada (múltiples skins, escalado fino por resolución/aspect ratio, fast-forward automatizado) es una **mejora de Fase 2**, a abordar recién una vez que el núcleo funcional (Fase 1, ver sección 12) esté probado y funcionando de forma 100% confiable en el bar, sin errores.

## 2. Entorno técnico

- SO: Windows 10
- Frontend: RetroBat (fork de EmulationStation), en modo kiosko
- Lenguaje sugerido para el programa: **C# (.NET, WinForms o WPF)**, por su integración nativa con WinAPI (hooks de teclado, overlays topmost, `SendInput`) y su bajo overhead como proceso de fondo. Python es una alternativa viable pero menos prolija para estas necesidades.
- Emuladores/sistemas a soportar:
  - **RetroArch** (Grupo A — ver sección 4): cores como Flycast (Dreamcast), SwanStation (PS1, probablemente), y otros.
  - **Standalone** (Grupo B): Dolphin, PCSX2, DuckStation (como respaldo, aunque probablemente DuckStation termine usándose vía core SwanStation dentro de RetroArch), Redream (a confirmar si termina usándose; Dreamcast probablemente se resuelva vía Flycast en RetroArch en su lugar por eficiencia en hardware viejo).
  - Juegos de PC nativos (caso más variable, sin mecanismo de pausa estándar garantizado).
- Hardware: PC vieja pero funcional, con **CRT-emudrivers** instalados (indica salida a un monitor CRT nativo), suficiente para correr Dreamcast vía RetroArch con buena eficiencia.

## 3. Arquitectura general

### 3.1. Referencia de arquitectura: RetroBat/EmulationStation

Vale la pena estudiar los scripts de RetroBat (repo open source en GitHub) para entender cómo alterna entre frontend y emulador sin conflictos. Su mecanismo no es complejo:

1. EmulationStation lanza el emulador como **proceso hijo** (`Process.Start()`), con argumentos definidos en `es_systems.cfg`.
2. ES queda en espera (`WaitForExit()` o equivalente) y se oculta/minimiza mientras el hijo corre.
3. Al terminar el proceso hijo, ES retoma el foco automáticamente.

Este patrón ("padre espera a que el hijo termine") es útil para que el programa de crédito sepa con certeza cuándo un emulador está realmente activo y en primer plano, sin depender de polling agresivo.

### 3.2. Módulos del programa

1. **Detector de procesos**: identifica cuándo se lanza un emulador o juego de PC nativo, usando `System.Diagnostics.Process` o WMI (`Win32_ProcessStartTrace` para detección en tiempo real). Debe distinguir cuándo el emulador está en primer plano (`GetForegroundWindow` + `GetWindowThreadProcessId`) para no interferir nunca con el menú de EmulationStation.

2. **Clasificador arcade vs no-arcade**: lee la lista única de favoritos de RetroBat (guardada en los `gamelist.xml` de cada sistema, marcados con `<favorite>true</favorite>`, o en un archivo de colección tipo `custom-Favorites.cfg` según la versión). Cruza cada juego con su carpeta de sistema (`mame`, `fbneo`, `arcade` = lista blanca exenta del sistema de crédito; todo lo demás = candidato a llevar overlay).

   **Lista blanca de sistemas arcade exentos** (además de MAME): Neo Geo, FBNeo/FB (FinalBurn Neo), Naomi, Atomiswave, Model 2, Model 3, TeknoParrot. Todo lo demás (Dreamcast vía Flycast con juegos de consola, GameCube, PS1/PS2, Wii, sistemas de cartucho como NES/SNES/Mega Drive/PC Engine, y PC nativo) queda dentro del sistema de crédito.

   Nota: muchos juegos de Capcom (CPS1/CPS2/CPS3) suelen correr por FBNeo en vez de por MAME cuando la versión de MAME instalada no los soporta o falta algún set específico. Como la clasificación es por carpeta de sistema y no por publisher, esos juegos ya quedan cubiertos automáticamente por estar bajo `fbneo`, sin necesidad de ningún ajuste adicional.

3. **Configuración por juego (.ini por ROM + defaults por sistema)**: cada sistema tiene valores por defecto en `config.ini`. El .ini individual de cada ROM se genera automáticamente (al primer lanzamiento, o en bloque al escanear la carpeta de un sistema) pero **nace con `activo=0`**, por lo que el default del sistema manda hasta que el usuario lo activa y personaliza a mano. Ver sección 5 para el detalle completo.

4. **Integración de pausa/control por emulador** (ver sección 4): pausa la emulación al mostrar el overlay, y la reanuda al insertar crédito, sin pérdida de progreso (no es un save state, es pausa real).

5. **Overlay**: ventana sin bordes, topmost, semitransparente, mostrando el mensaje de "insertar crédito" (tipo "INSERT COIN TO CONTINUE"). El overlay incluye un **countdown de 20 segundos** (configurable) para insertar crédito. **Fase 1 (MVP):** texto simple, fuente de sistema básica, sin sofisticación — solo debe ser legible y funcional. **Fase 2 (mejora futura):** soporte de **múltiples skins** seleccionables (carpetas con imagen de fondo + config de fuente/color, referenciadas desde el .ini) y adaptación fina de resolución (ver sección 8), a implementar recién cuando el núcleo funcional esté validado como estable.

6. **Salida automática por abandono**: si el countdown del overlay llega a cero sin que se inserte crédito, el programa asume que el cliente abandonó la partida y **cierra el emulador/juego automáticamente**, devolviendo el control al menú de RetroBat. El mecanismo de cierre depende del grupo:
   - **Grupo A (RetroArch)**: comando `QUIT` por la Network Command Interface (cierre limpio del proceso).
   - **Grupo B (standalone) y juegos de PC**: simulación de la hotkey de salida configurada en cada emulador/juego (por ejemplo Alt+F4 como respaldo genérico si no hay hotkey nativa de salida limpia), o si no responde, `Process.Kill()` como último recurso para garantizar que el sistema vuelva al frontend.
   - Este mecanismo es clave para que la máquina no quede "trabada" esperando un crédito que nunca llega, liberándola automáticamente para el siguiente cliente del bar.

6. **Sistema de crédito**: un hook global de teclado (`SetWindowsHookEx` con `WH_KEYBOARD_LL`) escucha la tecla que la controladora física del monedero (tipo IPAC/JPAC) traduce a una pulsación, pero **solo mientras el overlay está visible**, para no interferir con el resto del sistema en otros momentos.

7. **Loop de reinserción**: un timer que, tras insertar crédito, se resetea y vuelve a mostrar el overlay al cabo de un intervalo configurable (ej. cada 20 minutos), repitiendo la pausa.

8. **(Opcional) Fast-forward automatizado durante intros largas**: para juegos que tardan mucho en llegar a la pantalla de "press start" (ej. Mario Kart Wii), acelerar automáticamente la emulación desde el lanzamiento hasta un momento configurable (unos segundos antes de que aparezca el primer overlay), y restablecer la velocidad normal antes de pausar. Es **opcional por sistema/juego**: si un sistema no soporta fast-forward o no se configura, el programa simplemente omite ese paso y pasa directo a esperar el tiempo de overlay.

## 4. Mecanismos de pausa y fast-forward por emulador

### Grupo A — RetroArch (todos los cores: Flycast, SwanStation, etc.)

- **Pausa**: comando `PAUSE_TOGGLE` vía la **Network Command Interface** de RetroArch (socket UDP). No depende del foco de ventana ni de timing del SO — le habla directamente al núcleo. Requiere tener habilitado `network_cmd_enable = true` en la config de RetroArch (se deja fijo una sola vez).
- **Fast-forward**: comando `FAST_FORWARD` (toggle) por la misma interfaz de red.
- Es el mecanismo más confiable de todos los soportados. Al ser un solo tipo de integración, sirve para todos los sistemas que corran como core de RetroArch sin necesitar lógica especial por sistema.

### Grupo B — Standalone (Dolphin, PCSX2, DuckStation standalone)

- **Pausa**: simulación de la hotkey nativa de pausa de cada emulador (configurable en cada uno), usando `SendInput` de WinAPI (preferido sobre `PostMessage`, porque algunos emuladores en modo exclusive fullscreen con DirectInput no responden bien a `PostMessage`). Conviene forzar foco a la ventana del emulador (`SetForegroundWindow`) justo antes de enviar la tecla.
- **Fast-forward/turbo**: mismo enfoque, usando la hotkey de turbo nativa de cada emulador (por defecto, mantener presionada una tecla como Tab en Dolphin/PCSX2). Se simula key-down al iniciar el período de aceleración y key-up al terminarlo.
- **Riesgos a validar en pruebas**: pequeño desfasaje de frames al pausar (imperceptible), posible "pop" de audio al pausar/reanudar (cosmético), variabilidad de compatibilidad de `SendInput` según configuración de input de cada emulador instalado.

### Juegos de PC nativos

- Caso más variable: no hay mecanismo de pausa universal. Alternativas a evaluar:
  - Probar tecla de pausa estándar (`Pause`/`Esc`) si el juego la soporta.
  - Si no, cubrir la pantalla con el overlay y bloquear input (hook global de teclado/mouse) mientras se muestra el overlay, sin pausar el juego realmente (el juego sigue corriendo detrás, pero el jugador no puede interactuar hasta insertar crédito).

## 5. Estructura de archivos y configuración

### Ubicación: carpeta propia dentro de RetroBat

Todos los archivos del programa viven dentro de una carpeta propia en RetroBat (ej. `RetroBat\monedero\`), **nunca dentro de las carpetas de ROMs** — esto evita que RetroBat escanee los .ini como si fueran juegos y ensucie los gamelists, y deja todo el estado del programa autocontenido en un solo lugar, fácil de respaldar o borrar sin tocar nada de la instalación de RetroBat. El ejecutable puede correr en segundo plano desde el inicio de Windows o desde la bandeja del sistema; arranque desde donde arranque, resuelve sus rutas relativas a su propia ubicación.

```
RetroBat\
└── monedero\
    ├── monedero.exe
    ├── config.ini                          (defaults por sistema — ver más abajo)
    ├── dreamcast\
    │   ├── Sonic Adventure 2 (Europe).ini
    │   └── Crazy Taxi (USA).ini
    ├── psx\
    │   └── Soul Blade (Europe).ini
    └── wii\
        └── Mario Kart Wii.ini
```

### Jerarquía de configuración: default por sistema → override por ROM

`config.ini` contiene una sección por sistema con los valores por defecto que aplican a **todas las ROMs de ese sistema que no tengan su propio .ini individual**. Ejemplo:

```ini
[dreamcast]
tiempo_overlay=20
intervalo_loop=1200
tiempo_countdown=15
fast_forward=false
skin=default

[wii]
tiempo_overlay=25
intervalo_loop=1200
tiempo_countdown=20
fast_forward=false
skin=default
```

Si un juego puntual necesita valores distintos (por ejemplo, Mario Kart Wii tarda más que el resto de los juegos de Wii en llegar a la pantalla de título), se genera o se edita su .ini individual dentro de la carpeta del sistema, y esos valores tienen prioridad sobre el default de `config.ini` — pudiendo sobrescribir solo los campos necesarios, heredando el resto del default del sistema.

### Un .ini por ROM, agrupados por carpeta de sistema

En vez de un único .ini con secciones, **cada ROM tiene su propio archivo .ini**, dentro de la carpeta de su sistema (nombrada con el identificador de RetroBat). El nombre del .ini coincide con el nombre del archivo de la ROM, de modo que al entrar a la carpeta de un sistema se ve una lista de .ini que refleja la lista de ROMs, y basta hacer clic en uno para editar sus valores.

Ventajas de este esquema:
- Cada archivo es independiente: al guardar desde la interfaz no hay riesgo de pisar o corromper la configuración de otros juegos.
- Correspondencia visual directa entre ROM y su configuración.
- Edición manual trivial: se abre el .ini del juego con cualquier editor de texto.

Contrapartida menor: con cientos de ROMs por sistema se generan cientos de archivos pequeños, pero al ser de pocas líneas cada uno y estar agrupados por carpeta de sistema, se mantiene navegable.

### Contenido de cada .ini de ROM

Estructura propuesta (ejemplo ilustrativo, a refinar):

Ejemplo del contenido de `wii\Mario Kart Wii.ini`:

```ini
[monedero]
activo=1
tiempo_overlay=40
intervalo_loop=1200
tiempo_countdown=20
fast_forward=true
tiempo_fast_forward=38
skin=default
```

Ejemplo del contenido de `dreamcast\Sonic Adventure 2 (Europe).ini`:

```ini
[monedero]
activo=1
tiempo_overlay=20
intervalo_loop=1200
tiempo_countdown=20
fast_forward=false
skin=neon_arcade
```

- El sistema y el juego quedan identificados por la ruta del archivo (carpeta de sistema + nombre de archivo), no por el encabezado de sección.
- `activo`: interruptor manual (`1`/`0`) para habilitar o deshabilitar el sistema de crédito en ese juego puntual, independientemente de la clasificación automática por sistema/proceso. Útil, por ejemplo, para casos donde un sistema normalmente exento (como MAME) corre bajo MESS algún software que en la práctica es una consola de cartucho (NES/SNES/PC Engine), o al revés, para forzar la exclusión manual de un caso puntual sin tocar la lógica de clasificación general. Con `activo=0`, el programa reconoce la sección pero no aplica overlay, pausa ni countdown — el juego corre libre. Valor por defecto: `1`.
- `tiempo_overlay`: segundos desde el lanzamiento hasta que aparece el primer overlay de "insertar crédito".
- `intervalo_loop`: segundos entre reapariciones del overlay tras insertar crédito.
- `tiempo_countdown`: segundos que el overlay espera para recibir crédito antes de cerrar el emulador/juego automáticamente y volver al menú de RetroBat (valor por defecto: 20).
- `fast_forward` / `tiempo_fast_forward`: opcionales, solo si el sistema lo soporta.
- `skin`: referencia a la carpeta/tema visual del overlay para ese juego.

### Valores por defecto de `tiempo_overlay` según sistema/generación

A mayor capacidad de procesamiento de la consola, mayor suele ser el tiempo de carga de BIOS/logos/intro antes de llegar a la pantalla de título. Se proponen los siguientes valores base para la generación automática del .ini, ajustables luego por juego específico:

| Sistema | `tiempo_overlay` por defecto |
|---|---|
| NES, SNES, Mega Drive, PC Engine | 5–10 seg |
| PS1, Dreamcast, GameCube, PS2, Wii (PS1 en adelante) | 20 seg |

Nota: Wii es, en la práctica, el sistema que más tarda de todos los soportados — es probable que varios juegos de esa plataforma necesiten un `tiempo_overlay` por juego bastante mayor al default de 20 seg (ver ejemplo de Mario Kart Wii en la sección 5, con 40 seg).

### Identificación única de cada juego (clave del .ini) — caso ISOs (PS1, PS2, GameCube, Dreamcast)

En sistemas que usan ISOs/CHDs, el emulador identifica internamente el contenido por un ID/serial propio del disco (ej. formato SLES/SLUS en PS1/PS2), que **puede diferir entre regiones de un mismo juego** (ej. "Soul Blade" versión Europa vs. versión USA tienen serial distinto), incluso cuando para el usuario es "el mismo juego". No es necesario ni conveniente que el programa lea ese ID interno (requeriría parsear el header de cada ISO según sistema).

En cambio, la clave del .ini se arma con el **identificador de sistema de RetroBat** (`psx`, `dreamcast`, `gamecube`, `wii`, `snes`, etc. — tal cual los nombra RetroBat en sus carpetas de ROMs y en `es_systems.cfg`) más el **nombre de archivo real** del ROM/ISO, ej. `psx|Soul Blade (Europe).bin` vs `psx|Soul Blade (USA).bin`.

**Por qué el sistema y no el proceso del emulador**: atar la configuración al ejecutable (`retroarch.exe`, `duckstation-qt.exe`) es frágil — si el usuario cambia PS1 de SwanStation dentro de RetroArch a DuckStation standalone, toda la configuración de ese sistema se rompería. El sistema sigue llamándose `psx` sin importar qué programa ejecute sus ROMs, así que la configuración del monedero sobrevive a cambios de emulador. El proceso del emulador sigue siendo un dato necesario en tiempo de ejecución (para saber **cómo** pausar: red UDP vs hotkey simulada — ver sección 4), pero se resuelve por separado al detectar el lanzamiento, no forma parte del identificador de configuración.

**Correspondencia ROM → sistema**: se obtiene directamente de la estructura de carpetas de RetroBat (`roms\psx\`, `roms\dreamcast\`, etc.), que ya define a qué sistema pertenece cada archivo. Tanto al escanear una carpeta como al detectar un lanzamiento en tiempo real, el sistema sale de ahí.

El nombre de archivo se usa sin la ruta completa, para que sea portable si el usuario mueve su carpeta de ROMs. El naming estándar de dumps (Redump/No-Intro) casi siempre incluye la región en el nombre del archivo, por lo que versiones distintas del mismo juego generan secciones distintas automáticamente. En sistemas de ROMs (cartucho), el nombre de archivo suele ser aún más explícito respecto a versión/región, por lo que el mismo esquema aplica sin cambios.

**Lógica de resolución al lanzar un juego** (Fase 1): el programa identifica el sistema (por la carpeta de ROMs) y el nombre de archivo del juego lanzado, y busca `monedero\<sistema>\<nombre_de_archivo>.ini`.
- Si **no existe todavía**, se genera automáticamente con `activo=0` y el resto de los campos vacíos o comentados (a modo de plantilla lista para editar), y en ese mismo lanzamiento se usan los valores de la sección `[<sistema>]` de `config.ini`.
- Si **existe pero `activo=0`** (el estado por defecto al generarse): se ignoran sus valores y se usan los de `config.ini` para ese sistema — el archivo individual existe pero no tiene efecto todavía.
- Si **existe y `activo=1`** (el usuario lo activó y personalizó a mano): sus valores tienen prioridad sobre el default del sistema, pudiendo sobrescribir solo los campos que le interesen y heredar el resto del default.

Esta generación también ocurre en bloque al asignar la carpeta de ROMs de un sistema recién creado (ver sección 11, escaneo por carpeta): se generan todos los .ini de esa carpeta de una sola vez, todos con `activo=0`, listos para que el usuario decida cuáles personalizar.

## 8. Adaptación visual del overlay a cualquier resolución (Fase 2 — mejora futura, no indispensable para el MVP)

*Esta sección completa aplica recién después de validar que el núcleo funcional (Fase 1) es 100% estable y sin errores. Para la primera versión funcional, un texto simple con fuente de sistema básica es suficiente, sin necesidad de resolver escalado fino por resolución.*

Problema a resolver: el overlay se dibuja encima de la resolución en la que esté corriendo el emulador en ese momento, que puede variar enormemente — desde 240p/288p nativo de CRT (vía CRT-emudriver) hasta resoluciones HD o super-resoluciones de Windows. Un diseño que intente escalar dinámicamente texto vectorial se ve mal en los extremos (borroso en baja resolución, pixelado sin querer en alta), y casos de aspect ratio extremo (ej. una resolución tipo 2560x240, ancha y baja) rompen cualquier layout pensado con proporciones "normales", deformando el texto si se estira de forma dispareja en cada eje.

**Enfoque adoptado — inspirado en el RGUI de RetroArch**: en vez de diseñar el overlay para escalar dinámicamente o mantener sets de assets separados por rango de resolución, se sigue el mismo principio que usa RGUI (y los sistemas retro originales): todo el contenido del overlay se dibuja primero sobre un **canvas virtual interno de resolución fija** (ej. 320x240), con la fuente de mapa de bits y el fondo diseñados una única vez para ese tamaño exacto, sin depender de la resolución real de salida. Luego, ese frame completo se escala como una sola imagen hacia la resolución de pantalla real detectada en el momento, usando:

- **Escalado nearest-neighbor** (no bilineal/suavizado), para mantener el look pixel-art nítido en vez de verse borroso.
- **Corrección de aspect ratio conocida** (ej. 4:3), no un cálculo ingenuo de ancho/alto crudo — de la misma forma en que CRT-emudriver ya corrige píxeles no cuadrados a nivel de hardware para resoluciones "raras" como 2560x240 (que sigue representando lógicamente una imagen 4:3 aunque el conteo de píxeles no lo sugiera a simple vista), el overlay debe aplicar esa misma corrección al momento de escalar, en vez de estirar el canvas de forma dispareja en cada eje.

**Tabla de resoluciones conocidas** (en vez de un parámetro configurable genérico): el conjunto de resoluciones reales que va a manejar el cabinet es finito y predecible, no arbitrario — los sistemas retro trabajan a 224/240p nativo (ej. 320x240, 384x224), o a super-resolución CRT (ej. 2560x240) o vía HDMI con line-doubling (ej. ~1920x240), todos representando lógicamente un aspect ratio 4:3; los juegos de PC nativos y monitores modernos trabajan en resoluciones HD/4K estándar (16:9). El programa mantiene una tabla de referencia con estos casos conocidos y su aspect ratio físico correcto ya resuelto:

| Resolución detectada | Aspect ratio físico | Contexto |
|---|---|---|
| 320x240, 384x224 | 4:3 | Salida CRT nativa baja |
| 2560x240 | 4:3 | Super-resolución CRT |
| ~1920x240 (HDMI 240p / line-doubled) | 4:3 | Salida vía HDMI |
| 1280x720, 1920x1080, etc. | 16:9 | Monitor moderno / PC nativo |
| 3840x2160, etc. | 16:9 | 4K / alta densidad |

Si se detecta una resolución fuera de la tabla, se aplica una **heurística de respaldo en dos ramas**, pensada para que el programa nunca produzca una distorsión arbitraria ante configuraciones de otros usuarios que no fueron probadas (relevante de cara a la publicación open source, donde no todos van a configurar sus resoluciones ni sus juegos de PC nativos/consolas 3D de la misma forma):
- Si la altura detectada es muy baja (aprox. < 300px): asumir aspect ratio **4:3** por defecto, ya que prácticamente todo cabinet arcade con salida CRT nativa apunta a 4:3 independientemente del modo de super-resolución horizontal particular de cada usuario — es una convención real del hardware, no una suposición al azar.
- Si la altura es "normal" (monitor moderno, aprox. > 480px): redondear al aspect ratio estándar más cercano de una lista corta conocida (4:3, 16:10, 16:9, 21:9), en vez de confiar en el cálculo crudo de ancho/alto, que podría amplificar alguna configuración atípica no estándar.

La tabla es editable/extensible para adaptarse a otros setups CRT de la comunidad al publicar el proyecto.

**Excepción para resoluciones muy altas (PC nativo en HD/4K)**: escalar un canvas de baja resolución (ej. 320x240) con nearest-neighbor hacia 4K produce un resultado proporcionalmente correcto pero visualmente "bloque" (pixel-art muy grande), lo cual puede sentirse fuera de lugar sobre un juego de PC con gráficos modernos. Por encima de un umbral configurable (ej. >1440p de alto), el overlay usa en su lugar una **fuente vectorial nítida** (TrueType con antialiasing) a tamaño de punto grande, en vez de escalar el bitmap de baja resolución — solo para ese rango superior, el resto del espectro sigue usando el canvas fijo + escalado nearest-neighbor.

**Detección de resolución**: se consulta la resolución de pantalla real activa justo antes de mostrar el overlay (`EnumDisplaySettings` / `Screen.PrimaryScreen.Bounds`), ya que con CRT-emudriver la resolución del sistema cambia efectivamente al lanzar distintos sistemas/juegos, no es una resolución de escritorio fija.

**Ventaja sobre un sistema de múltiples modos/assets por rango de resolución** (enfoque descartado): un solo diseño calibrado al canvas virtual sirve para cualquier resolución de salida, porque el trabajo de adaptación lo hace el paso de escalado final (una sola operación de dibujo con el rectángulo destino correcto), no un rediseño manual por rango. Esto también simplifica el sistema de skins: cada skin es un único set de assets (fondo + fuente de mapa de bits) calibrado al canvas virtual, sin necesidad de subcarpetas por modo de resolución.

## 10. Riesgos y fisuras a resolver (validar durante la Fase 1)

Puntos que todavía no están resueltos en el diseño y que conviene tener en mente porque afectan directamente a la prioridad de estabilidad/simplicidad del MVP:

1. **Overlay sobre fullscreen exclusivo**: si algún emulador o juego de PC corre en modo *fullscreen exclusivo* (no "sin bordes"/borderless), Windows puede impedir que cualquier otra ventana (incluida una topmost) se dibuje por encima — es el mismo problema histórico que tuvieron los overlays de Steam/Discord sobre juegos en exclusive fullscreen. Antes de asumir que el overlay "simplemente aparece encima", hay que validar esto por emulador, y si hace falta, configurar cada emulador en modo *borderless windowed* (la mayoría lo soportan) en vez de exclusive fullscreen.

2. **Riesgo de corrupción de memory card / battery save al forzar el cierre**: si el countdown se agota y el programa mata el proceso del emulador (`Process.Kill()`) en el peor momento (por ejemplo, mientras se está escribiendo en la memory card virtual de PS1/PS2 o en la SRAM de un cartucho), se puede corromper el archivo de guardado — y como es un cabinet compartido, eso podría afectar guardado de **otros juegos** que usan la misma memory card. Mitigación: siempre intentar primero un cierre limpio (comando `QUIT` en RetroArch, o hotkey de salida nativa en standalone) con un pequeño margen de espera, y usar `Process.Kill()` solo como último recurso si el cierre limpio no responde en un tiempo razonable (ej. 2-3 segundos).

3. **Scripts propios de RetroBat al finalizar un juego**: RetroBat/EmulationStation puede ejecutar acciones al detectar que el emulador terminó (actualizar "última vez jugado" en el gamelist, generar capturas, etc.). Si el cierre es abrupto (kill) en vez de limpio, esos scripts podrían no dispararse correctamente, o en el peor caso EmulationStation podría interpretar el cierre como un "crash" y mostrar algún diálogo — rompiendo la experiencia de kiosko. Reforzar el punto anterior (cierre limpio primero) ayuda a mitigar esto también.

4. **Watchdog del propio programa**: si `monedero.exe` crashea justo mientras un juego está pausado con el overlay visible, la máquina queda "trabada" indefinidamente (el emulador nunca se reanuda ni se cierra). Conviene un mecanismo simple de auto-recuperación: un proceso liviano separado (o un temporizador interno con manejo de excepciones robusto) que detecte si el programa principal dejó de responder y lo reinicie, o al menos que fuerce el cierre del emulador pausado para no dejar la máquina inutilizable hasta que alguien intervenga manualmente.

5. **Pulsos múltiples del monedero físico**: algunos mecanismos de moneda (vía IPAC/JPAC) pueden enviar más de un pulso eléctrico (y por lo tanto más de una pulsación de tecla) por cada moneda física insertada, dependiendo de cómo esté calibrado el mecanismo. Conviene que el .ini general tenga un parámetro tipo `pulsos_por_credito` para que el programa cuente pulsos y no asuma 1 pulsación = 1 crédito de forma rígida.

6. **Rendimiento del hook de teclado global**: un hook `WH_KEYBOARD_LL` mal implementado (callback lento) puede hacer que Windows lo deshabilite automáticamente si no responde rápido. Hay que asegurar que el callback sea liviano y no bloquee en I/O (ej. no escribir al .ini directamente desde el callback del hook).

7. **Dependencia de la estructura interna de RetroBat**: el programa depende de convenciones actuales de RetroBat (nombres de carpetas de sistema, formato de `gamelist.xml`/favoritos, comportamiento de `es_systems.cfg`). Si una actualización futura de RetroBat cambia alguna de estas convenciones, el programa podría dejar de clasificar bien los sistemas — implica que el proyecto (sobre todo de cara a publicarse) va a necesitar mantenimiento ocasional para seguir compatible con nuevas versiones de RetroBat.

8. **Seguridad física del kiosko**: nada de esto evita que un cliente con acceso físico a un teclado conectado al cabinet pueda, en teoría, cerrar `monedero.exe` desde el Administrador de tareas si el modo kiosko de Windows no está bien bloqueado. Esto está fuera del alcance del programa en sí, pero vale la pena tenerlo presente como parte de la configuración general del kiosko (deshabilitar Ctrl+Alt+Supr, Administrador de tareas, etc., a nivel de Windows).

## 11. Consideraciones de diseño para publicación open source

- **Genérico desde el diseño**: nada de rutas absolutas ni listas de juegos hardcodeadas; todo debe detectarse dinámicamente desde los favoritos de RetroBat de cada usuario.
- **Licencia**: a definir (sugerencia: MIT por simplicidad, o GPL si se quiere forzar que los forks también sean libres).
- **Documentación**: README con requisitos (versión de RetroBat, .NET runtime), instalación, cómo se genera/edita el .ini, hotkeys por emulador soportado y cómo remapearlas.
- **Empaquetado**: instalador simple o .zip con el .exe + un .ini de ejemplo comentado.
- **Nombre del proyecto**: pendiente de definir, útil para el repo y la documentación.

## 12. Estado actual y próximos pasos

Hasta el momento se definió el diseño conceptual completo (arquitectura, mecanismos de pausa por grupo de emuladores, formato de configuración, generación automática, mejoras visuales futuras, y consideraciones de publicación). **Aún no se escribió código.**

### Fase 1 — MVP indispensable (prioridad: eficiencia, simpleza, cero conflictos)

El objetivo de esta fase es tener el ciclo básico funcionando de forma 100% confiable, sin errores ni conflictos con el emulador o con RetroBat: detectar el juego → contar tiempo → mostrar aviso simple de "insertar crédito" + countdown → pausar mientras tanto → reanudar si hay crédito, o cerrar el emulador si se agota el tiempo → repetir en loop.

1. Detector de procesos + clasificación arcade/no-arcade vía favoritos de RetroBat (lista blanca de sistemas exentos).
2. Lectura de `config.ini` (defaults por sistema) y generación automática (con `activo=0`) + lectura del .ini individual por ROM — ver sección 5.
3. Integración de pausa por red con RetroArch (`PAUSE_TOGGLE`, `QUIT`) como primer caso de prueba — es el más simple y confiable.
4. Overlay mínimo funcional: texto simple ("INSERT COIN"), fuente de sistema básica, countdown visible, sin skins ni escalado sofisticado.
5. Lógica de countdown + salida automática del emulador al agotarse el tiempo sin crédito.
6. Loop de reinserción de crédito (`intervalo_loop`).
7. Hook de teclado para el input físico del monedero (IPAC/JPAC).
8. Extender la integración de pausa/salida a los emuladores standalone (Dolphin, PCSX2, DuckStation) con simulación de hotkeys.
9. Implementar cierre limpio con margen de espera antes de recurrir a `Process.Kill()` (ver sección 10, punto 2) para evitar corrupción de memory cards/saves compartidas.
10. Implementar un mecanismo simple de watchdog para que el programa no deje la máquina trabada si crashea mientras un juego está pausado (ver sección 10, punto 4).
11. Soportar `pulsos_por_credito` en la configuración, en caso de que el mecanismo físico de moneda envíe más de un pulso por moneda (ver sección 10, punto 5).
12. Probar exhaustivamente en la PC real del bar, ajustando tiempos por juego en el .ini, hasta confirmar que no hay errores ni conflictos con RetroBat/EmulationStation en ningún escenario — incluyendo el comportamiento sobre emuladores en fullscreen exclusivo (ver sección 10, punto 1).

### Fase 2 — Mejoras (recién después de validar que la Fase 1 es 100% estable)

1. Fast-forward automatizado durante intros largas (opcional por juego/sistema).
2. Sistema de múltiples skins visuales para el overlay.
3. Adaptación visual fina por resolución/aspect ratio (canvas virtual + escalado, tabla de resoluciones conocidas — ver sección 8).
4. **Interfaz de configuración oculta** (ver sección 13).
5. Documentación y empaquetado para publicación open source (README, licencia, instalador).

## 13. Interfaz de configuración oculta (Fase 2)

Capa de conveniencia sobre el mismo .ini, que **convive con la edición manual** (el .ini sigue siendo la fuente de verdad y se puede seguir editando con cualquier editor de texto). Se activa con una tecla/combinación desde el cabinet, sin salir del modo kiosko.

**Funciones:**
- Configuración general del loop: valores globales por defecto (`tiempo_countdown`, `intervalo_loop`) aplicables salvo override por juego.
- **Pestañas por sistema, creadas bajo demanda**: un botón "+" / "agregar sistema" permite crear la pestaña de un sistema (ej. `psx`), usando el **nombre exacto con que RetroBat identifica ese sistema**, no el del emulador. Dentro de la pestaña se configuran los defaults del monedero para todo ese sistema, que luego heredan todas sus ROMs (con posibilidad de override individual por juego).
- **Escaneo por carpeta de ROMs**: pre-genera las secciones del .ini para todos los ROMs de un sistema en una sola pasada, sin necesidad de ejecutar cada juego para que se cree su entrada.
- **Independencia de la lista de favoritos**: al agrupar por sistema y permitir escanear la carpeta de ROMs directamente, el programa funciona igual para usuarios que no usen una colección de favoritos — basta con agregar la pestaña del sistema y escanear su carpeta para tener el monedero configurado en todo ese sistema.
- **Edición contextual del juego en curso**: como el programa ya sabe en todo momento qué juego está corriendo (mismo dato usado para el sistema de crédito), al abrir la interfaz con un juego activo aparece un botón tipo "editar configuración de este juego", sin necesidad de buscarlo manualmente. Dos formas de editar, ambas válidas:
  - Abrir el archivo .ini real en el editor de texto por defecto de Windows, en primer plano.
  - Un formulario dentro de la misma interfaz con los campos ya cargados (`tiempo_overlay`, `tiempo_countdown`, `intervalo_loop`, etc.), para editar sin salir del programa.
  Al guardar desde cualquiera de las dos formas, el programa **activa automáticamente `activo=1`** en el .ini de ese juego — si el usuario editó valores a mano con intención, el override debe tener efecto inmediato, no quedar ignorado por seguir en `activo=0`.

**Consideraciones de diseño:**
- **Tecla de activación**: en un cabinet de bar en modo kiosko, conviene evitar teclas simples tipo F5 (un cliente con acceso a un teclado podría abrir la config y regalarse créditos). Preferible una combinación poco probable de pulsar por accidente (ej. Ctrl+Alt+F5) e idealmente protegida con un PIN simple configurable.
- **Un solo proceso**: la interfaz debe ser una ventana del mismo ejecutable que corre en segundo plano, no un programa aparte — así el acceso de lectura/escritura al .ini queda centralizado en un único proceso y se evitan lecturas parciales o cambios pisados por concurrencia (si fueran dos ejecutables, habría que implementar locking, complejidad innecesaria).
- **Edición quirúrgica**: al guardar desde la interfaz, modificar solo las claves tocadas en vez de regenerar el archivo completo, para no perder comentarios ni secciones que el usuario haya escrito manualmente.
