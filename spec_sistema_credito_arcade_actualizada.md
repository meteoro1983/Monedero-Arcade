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

1. **Detector de procesos / sesiones**: identifica cuándo se lanza un emulador o juego de PC nativo, usando `System.Diagnostics.Process` o eventos/WMI (`Win32_ProcessStartTrace`) según convenga. No debe asumir una relación rígida entre juego y un único PID: mantiene una `GameSession` con sistema, ROM, tipo de integración y procesos relevantes. Debe distinguir cuándo la sesión está realmente activa y cuándo EmulationStation recuperó el control.

2. **Clasificador arcade vs no-arcade**: usa el **sistema RetroBat como fuente primaria**, identificado por la carpeta de ROMs y/o configuración de RetroBat. La lista de favoritos puede utilizarse como mecanismo opcional de descubrimiento, pero el funcionamiento del monedero no debe depender de ella. Clasifica cada juego según su sistema (`mame`, `fbneo`, `arcade` = lista blanca exenta del sistema de crédito; todo lo demás = candidato a llevar overlay). Los formatos de favoritos de RetroBat (`gamelist.xml`, colecciones, etc.) sólo se consultan cuando el usuario decide utilizarlos como mecanismo de descubrimiento/configuración.

   **Lista blanca de sistemas arcade exentos** (además de MAME): Neo Geo, FBNeo/FB (FinalBurn Neo), Naomi, Atomiswave, Model 2, Model 3, TeknoParrot. Todo lo demás (Dreamcast vía Flycast con juegos de consola, GameCube, PS1/PS2, Wii, sistemas de cartucho como NES/SNES/Mega Drive/PC Engine, y PC nativo) queda dentro del sistema de crédito.

   Nota: muchos juegos de Capcom (CPS1/CPS2/CPS3) suelen correr por FBNeo en vez de por MAME cuando la versión de MAME instalada no los soporta o falta algún set específico. Como la clasificación es por carpeta de sistema y no por publisher, esos juegos ya quedan cubiertos automáticamente por estar bajo `fbneo`, sin necesidad de ningún ajuste adicional.

3. **Configuración por juego (.ini por ROM + defaults por sistema)**: cada sistema tiene valores por defecto en `config.ini`. El `.ini` individual de cada ROM se genera automáticamente (al primer lanzamiento, o en bloque al escanear la carpeta de un sistema) y nace con `modo=heredar`. El usuario puede cambiarlo a `modo=forzar` para aplicar overrides o a `modo=desactivar` para excluir el juego. Ver sección 5 para el detalle completo.

4. **Integración de pausa/control por emulador** (ver sección 4): expone una interfaz común (`Freeze`, `Unfreeze`, `Quit` y, en Fase 2, `FastForward`) que oculta las diferencias entre RetroArch, emuladores standalone y juegos de PC. La sesión coordina overlay, bloqueo de input y pausa/reanudación para evitar estados intermedios inconsistentes.

5. **Overlay**: ventana sin bordes, topmost, semitransparente, mostrando el mensaje de "insertar crédito" (tipo "INSERT COIN TO CONTINUE"). El overlay incluye un **countdown de 20 segundos** (configurable) para insertar crédito. **Fase 1 (MVP):** texto simple, fuente de sistema básica, sin sofisticación — solo debe ser legible y funcional. **Fase 2 (mejora futura):** soporte de **múltiples skins** seleccionables (carpetas con imagen de fondo + config de fuente/color, referenciadas desde el .ini) y adaptación fina de resolución (ver sección 8), a implementar recién cuando el núcleo funcional esté validado como estable.

6. **Salida automática por abandono**: si el countdown del overlay llega a cero sin que se inserte crédito, el programa asume que el cliente abandonó la partida y **cierra el emulador/juego automáticamente**, devolviendo el control al menú de RetroBat. El mecanismo de cierre sigue una escalera: cierre nativo/limpio → esperar un margen breve → `Process.Kill()` sólo como último recurso. Durante `CLOSING` no se aceptan nuevos créditos. El mecanismo depende del grupo:
   - **Grupo A (RetroArch)**: comando `QUIT` por la Network Command Interface (cierre limpio del proceso).
   - **Grupo B (standalone) y juegos de PC**: simulación de la hotkey de salida configurada en cada emulador/juego (por ejemplo Alt+F4 como respaldo genérico si no hay hotkey nativa de salida limpia), o si no responde, `Process.Kill()` como último recurso para garantizar que el sistema vuelva al frontend.
   - Este mecanismo es clave para que la máquina no quede "trabada" esperando un crédito que nunca llega, liberándola automáticamente para el siguiente cliente del bar.

6. **Sistema de crédito**: un hook global de teclado (`SetWindowsHookEx` con `WH_KEYBOARD_LL`) escucha la tecla que la controladora física del monedero (tipo IPAC/JPAC) traduce a una pulsación, pero **solo mientras el overlay está visible**, para no interferir con el resto del sistema en otros momentos. El callback debe ser mínimo: detectar la pulsación, aplicar la lógica de normalización/debounce y encolar el evento para el hilo principal.

   El procesamiento debe contemplar `pulsos_por_credito` y, si fuese necesario, un pequeño período de debounce configurable para evitar que rebotes o pulsos duplicados conviertan una sola moneda en varios créditos.

7. **Loop de reinserción**: un temporizador lógico basado en tiempo monotónico (`Stopwatch`) que, tras insertar crédito, reinicia el período de juego y vuelve a mostrar el overlay al agotarse la duración configurada. El timer de UI sólo actualiza la pantalla; la medición real se basa en elapsed time para evitar drift.

**Regla temporal fundamental:** el reloj de Monedero es independiente de la pausa interna del juego. Si el jugador pulsa `Start` o utiliza cualquier función de pausa nativa del juego, el tiempo de crédito continúa corriendo. Cuando `duracion_credito` se agota, Monedero congela la emulación externamente, muestra el overlay y comienza el countdown. Ese período de espera no consume un nuevo crédito: el siguiente crédito comienza solamente cuando se detectan las monedas requeridas y se ejecuta `Unfreeze()`.

8. **(Opcional) Fast-forward automatizado durante intros largas**: para juegos que tardan mucho en llegar a la pantalla de "press start" (ej. Mario Kart Wii), acelerar automáticamente la emulación desde el lanzamiento hasta un momento configurable (unos segundos antes de que aparezca el primer overlay), y restablecer la velocidad normal antes de pausar. Es **opcional por sistema/juego**: si un sistema no soporta fast-forward o no se configura, el programa simplemente omite ese paso y pasa directo a esperar el tiempo de overlay.

### 3.3. Máquina de estados de una sesión

El núcleo debe modelar cada partida mediante una máquina de estados explícita para evitar carreras, dobles detecciones y transiciones ambiguas.

```text
IDLE
  │
  ├── detecta juego elegible
  ↓
RUNNING
  │
  ├── tiempo inicial / duración de crédito agotado
  ↓
WAITING_CREDIT
  │
  ├── crédito válido → RUNNING
  │
  └── countdown agotado → CLOSING
                           │
                           ↓
                         IDLE
```

La entrada a `WAITING_CREDIT` debe ser una operación lógica coordinada: preparar y mostrar el overlay, bloquear el input relevante y solicitar la pausa del emulador. Al recibir crédito, la sesión debe reanudar la emulación antes de liberar el input y retirar el overlay.

Una vez iniciado `CLOSING`, la sesión deja de aceptar créditos para esa partida y espera la finalización del proceso.

### 3.4. Identidad de sesión frente a identidad de proceso

El PID es un dato de ejecución, no la identidad permanente del juego. La sesión mantiene como referencia estable, en la medida posible:

- `systemId`: identificador de sistema de RetroBat (`psx`, `dreamcast`, `wii`, etc.).
- `romPath`: ruta del contenido lanzado.
- `romName`: nombre de archivo utilizado para resolver configuración.
- `emulatorType`: `retroarch`, `standalone` o `pc`.
- procesos relevantes y PID actual.
- estado de la sesión.

Esto permite conservar la configuración al cambiar de emulador y evita asumir una relación rígida entre juego y proceso.

### 3.5. Modo de prueba y diagnóstico

Desde el inicio debe existir un modo de prueba para calibración y desarrollo. Permite tiempos cortos, una tecla de crédito virtual, logging detallado y la posibilidad de desactivar temporalmente el `Process.Kill()` forzado.

Ejemplo de log:

```text
14:22:01 GAME START
14:22:21 CREDIT STATE
14:22:21 PAUSE REQUEST
14:22:21 PAUSE CONFIRMED
14:22:22 CREDIT RECEIVED
14:22:22 RESUME CONFIRMED
14:42:22 CREDIT STATE
14:42:42 TIMEOUT
14:42:42 QUIT REQUEST
14:42:43 PROCESS EXIT
```

En producción el logging debe poder reducirse o apagarse.

## 4. Mecanismos de pausa y fast-forward por emulador

### 4.1. Interfaz común de control

El núcleo no debe conocer detalles concretos de cada emulador. Los adaptadores implementan conceptualmente:

```text
Pause()
Resume()
Quit()
FastForwardStart()    # Fase 2
FastForwardStop()     # Fase 2
IsRunning()
```

Las diferencias entre NCI, hotkeys y mecanismos específicos quedan encapsuladas en cada adaptador. Así es posible agregar otro emulador sin modificar el `Session Manager`.

### Grupo A — RetroArch (todos los cores: Flycast, SwanStation, etc.)

- **Pausa**: comando `PAUSE_TOGGLE` vía la **Network Command Interface** de RetroArch (socket UDP). Requiere `network_cmd_enable = true` en la configuración de RetroArch.
- **Verificación de estado**: consultar `GET_STATUS` antes y/o después de una transición cuando sea posible, comprobando si RetroArch está realmente `PLAYING` o `PAUSED`.
- **Resume**: `Resume()` sólo debe enviar el toggle cuando el estado confirmado sea `PAUSED`, evitando toggles ciegos.
- **Quit**: comando `QUIT` por NCI para cierre limpio.
- **Fast-forward**: queda fuera del núcleo del MVP; en Fase 2 se encapsulará detrás de la interfaz común.
- Es el mecanismo preferido para todos los sistemas que corran como core de RetroArch.

### Grupo B — Standalone (Dolphin, PCSX2, DuckStation standalone)

- **Freeze/Unfreeze**: simulación de la hotkey nativa de pausa/congelación de cada emulador (configurable en cada uno), usando `SendInput` de WinAPI (preferido sobre `PostMessage`, porque algunos emuladores en modo exclusive fullscreen con DirectInput no responden bien a `PostMessage`). Conviene forzar foco a la ventana del emulador (`SetForegroundWindow`) justo antes de enviar la tecla.
- **Fast-forward/turbo**: mismo enfoque, usando la hotkey de turbo nativa de cada emulador (por defecto, mantener presionada una tecla como Tab en Dolphin/PCSX2). Se simula key-down al iniciar el período de aceleración y key-up al terminarlo.
- **Riesgos a validar en pruebas**: pequeño desfasaje de frames al pausar (imperceptible), posible "pop" de audio al pausar/reanudar (cosmético), variabilidad de compatibilidad de `SendInput` según configuración de input de cada emulador instalado.

### Juegos de PC nativos

- Caso más variable: no hay mecanismo universal de congelación externa. Alternativas a evaluar:
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
monedas_por_credito=1
tiempo_inicial=20s
duracion_credito=20m
countdown=15s
fast_forward=false
skin=default

[wii]
monedas_por_credito=1
tiempo_inicial=25s
duracion_credito=20m
countdown=20s
fast_forward=false
skin=default
```

Si un juego puntual necesita valores distintos (por ejemplo, Mario Kart Wii tarda más que el resto de los juegos de Wii en llegar a la pantalla de título), se genera o se edita su .ini individual dentro de la carpeta del sistema, y esos valores tienen prioridad sobre el default de `config.ini` — pudiendo sobrescribir solo los campos necesarios, heredando el resto del default del sistema.

### Unidades de tiempo y facilidad de configuración

Los tiempos deben configurarse en **unidades humanas**, sin exigir conversiones manuales a segundos. En la interfaz, el usuario debe poder seleccionar minutos y segundos de forma directa; el programa realiza internamente la conversión a la unidad temporal que utilice el motor.

Ejemplo de interfaz:

```text
Tiempo inicial:
[ 00 ] min [ 40 ] seg

Duración del crédito:
[ 20 ] min [ 00 ] seg

Countdown para insertar crédito:
[ 00 ] min [ 20 ] seg

Monedas por crédito:
[ 2 ]
```

Así, el usuario puede indicar "20 minutos" sin tener que saber que internamente equivalen a 1200 segundos. La interfaz debe encargarse de toda conversión, validación y presentación. El formato físico de almacenamiento del `.ini` puede utilizar segundos normalizados o una notación legible como `20m`; esa decisión se fija durante la implementación, pero nunca debe trasladarse la carga del cálculo al usuario.

### `monedas_por_credito`

La cantidad de monedas necesarias para conceder un crédito es un parámetro independiente y configurable por sistema y por juego. No modifica la lógica del programa: determina únicamente cuántas monedas válidas deben acumularse antes de conceder **un** crédito.

Por ejemplo, con `monedas_por_credito=2`: `moneda 1 → 1/2`, `moneda 2 → 2/2 → crédito concedido → Unfreeze → comienza el nuevo período`. Con `3`, se requieren tres monedas para el mismo crédito.

Esto permite que el propietario del bar configure, por ejemplo, un juego como Mario Kart o Virtua Tennis para requerir dos monedas por cada período de juego, mientras otros títulos requieren una sola. El código no necesita ninguna regla especial para esos juegos.

`monedas_por_credito` sigue la misma jerarquía de configuración: default global → default del sistema → override del juego. Debe mantenerse separado de parámetros físicos como `pulsos_por_moneda`, que describen cómo la controladora genera la señal eléctrica. El precio comercial de un crédito y el comportamiento físico del monedero son conceptos diferentes.

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
modo=forzar
monedas_por_credito=2
tiempo_inicial=40s
duracion_credito=20m
countdown=20s
fast_forward=true
tiempo_fast_forward=38s
skin=default
```

Ejemplo del contenido de `dreamcast\Sonic Adventure 2 (Europe).ini`:

```ini
[monedero]
modo=forzar
monedas_por_credito=1
tiempo_inicial=20s
duracion_credito=20m
countdown=20s
fast_forward=false
skin=neon_arcade
```

- El sistema y el juego quedan identificados por la ruta del archivo (carpeta de sistema + nombre de archivo), no por el encabezado de sección.
- `modo`: `heredar`, `forzar` o `desactivar`. `heredar` usa el default del sistema; `forzar` activa un override por juego; `desactivar` excluye el juego del monedero. Valor por defecto al generar un archivo nuevo: `heredar`. Es útil, por ejemplo, para excepciones puntuales donde un sistema normalmente exento necesite aplicar el monedero, o viceversa.
- `monedas_por_credito`: cantidad de monedas válidas necesarias para conceder un crédito. Puede cambiar por sistema o por juego sin cambiar la lógica del programa.
- `tiempo_inicial`: tiempo desde el lanzamiento hasta que aparece el primer overlay de "insertar crédito". El usuario lo configura en minutos/segundos; el programa realiza la conversión interna automáticamente.
- `duracion_credito`: duración del período concedido después de insertar la cantidad requerida de monedas. El usuario lo configura en minutos/segundos; el programa realiza la conversión interna. Al alcanzar este límite, Monedero congela la emulación y muestra nuevamente el overlay.
- `countdown`: tiempo que el overlay espera para recibir la cantidad requerida de monedas antes de cerrar el emulador/juego automáticamente y volver al menú de RetroBat. El usuario lo configura en minutos/segundos. El countdown es independiente del reloj de crédito.
- `fast_forward` / `tiempo_fast_forward`: opcionales, solo si el sistema lo soporta.
- `skin`: referencia a la carpeta/tema visual del overlay para ese juego.

### Valores por defecto de `tiempo_inicial` según sistema/generación

A mayor capacidad de procesamiento de la consola, mayor suele ser el tiempo de carga de BIOS/logos/intro antes de llegar a la pantalla de título. Se proponen los siguientes valores base para la generación automática del .ini, ajustables luego por juego específico:

| Sistema | `tiempo_inicial` por defecto |
|---|---|
| NES, SNES, Mega Drive, PC Engine | 5–10 seg |
| PS1, Dreamcast, GameCube, PS2, Wii (PS1 en adelante) | 20 seg |

Nota: Wii es, en la práctica, el sistema que más tarda de todos los soportados — es probable que varios juegos de esa plataforma necesiten un `tiempo_inicial` por juego bastante mayor al default de 20 seg (ver ejemplo de Mario Kart Wii en la sección 5, con 40 seg).

### Identificación única de cada juego (clave del .ini) — caso ISOs (PS1, PS2, GameCube, Dreamcast)

En sistemas que usan ISOs/CHDs, el emulador identifica internamente el contenido por un ID/serial propio del disco (ej. formato SLES/SLUS en PS1/PS2), que **puede diferir entre regiones de un mismo juego** (ej. "Soul Blade" versión Europa vs. versión USA tienen serial distinto), incluso cuando para el usuario es "el mismo juego". No es necesario ni conveniente que el programa lea ese ID interno (requeriría parsear el header de cada ISO según sistema).

En cambio, la clave del .ini se arma con el **identificador de sistema de RetroBat** (`psx`, `dreamcast`, `gamecube`, `wii`, `snes`, etc. — tal cual los nombra RetroBat en sus carpetas de ROMs y en `es_systems.cfg`) más el **nombre de archivo real** del ROM/ISO, ej. `psx|Soul Blade (Europe).bin` vs `psx|Soul Blade (USA).bin`.

**Por qué el sistema y no el proceso del emulador**: atar la configuración al ejecutable (`retroarch.exe`, `duckstation-qt.exe`) es frágil — si el usuario cambia PS1 de SwanStation dentro de RetroArch a DuckStation standalone, toda la configuración de ese sistema se rompería. El sistema sigue llamándose `psx` sin importar qué programa ejecute sus ROMs, así que la configuración del monedero sobrevive a cambios de emulador. El proceso del emulador sigue siendo un dato necesario en tiempo de ejecución (para saber **cómo** pausar: red UDP vs hotkey simulada — ver sección 4), pero se resuelve por separado al detectar el lanzamiento, no forma parte del identificador de configuración.

**Correspondencia ROM → sistema**: se obtiene directamente de la estructura de carpetas de RetroBat (`roms\psx\`, `roms\dreamcast\`, etc.), que ya define a qué sistema pertenece cada archivo. Tanto al escanear una carpeta como al detectar un lanzamiento en tiempo real, el sistema sale de ahí.

El nombre de archivo se usa sin la ruta completa, para que sea portable si el usuario mueve su carpeta de ROMs. El naming estándar de dumps (Redump/No-Intro) casi siempre incluye la región en el nombre del archivo, por lo que versiones distintas del mismo juego generan secciones distintas automáticamente. En sistemas de ROMs (cartucho), el nombre de archivo suele ser aún más explícito respecto a versión/región, por lo que el mismo esquema aplica sin cambios.

**Lógica de resolución al lanzar un juego** (Fase 1): el programa identifica el sistema (por la carpeta de ROMs) y el nombre de archivo del juego lanzado, y busca `monedero\<sistema>\<nombre_de_archivo>.ini`.
- Si **no existe todavía**, se genera automáticamente con `modo=heredar` y el resto de los campos vacíos o comentados (a modo de plantilla lista para editar), y en ese mismo lanzamiento se usan los valores de la sección `[<sistema>]` de `config.ini`.
- Si **existe pero `modo=heredar`** (el estado por defecto al generarse): se ignoran sus valores y se usan los de `config.ini` para ese sistema — el archivo individual existe pero no tiene efecto todavía.
- Si **existe y `modo=forzar`** (el usuario lo activó y personalizó a mano): sus valores tienen prioridad sobre el default del sistema, pudiendo sobrescribir solo los campos que le interesen y heredar el resto del default.

Esta generación también ocurre en bloque al asignar la carpeta de ROMs de un sistema recién creado (ver sección 11, escaneo por carpeta): se generan todos los .ini de esa carpeta de una sola vez, todos con `modo=heredar`, listos para que el usuario decida cuáles heredar, forzar o desactivar.

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

6. **Rendimiento del hook de teclado global**: un hook `WH_KEYBOARD_LL` mal implementado (callback lento) puede hacer que Windows lo deshabilite automáticamente si no responde rápido. El callback debe ser mínimo, sin I/O ni escritura de configuración; sólo identifica y encola el pulso para procesarlo en el hilo normal de la aplicación.

7. **Condiciones de carrera entre overlay, emulador y crédito**: el usuario puede insertar una moneda justo cuando termina el countdown, o el proceso puede cerrarse por sí solo mientras se muestra el overlay. La máquina de estados debe resolver estas transiciones de forma determinista: al entrar en `CLOSING` ya no se acepta crédito; si el proceso terminó, se destruye el overlay y la sesión pasa a `IDLE`; `Freeze()`/`Unfreeze()` deben ser idempotentes a nivel lógico.

8. **Dependencia de la estructura interna de RetroBat**: el programa depende de convenciones actuales de RetroBat (nombres de carpetas de sistema, formato de `gamelist.xml`/favoritos, comportamiento de `es_systems.cfg`). Si una actualización futura de RetroBat cambia alguna de estas convenciones, el programa podría dejar de clasificar bien los sistemas — implica que el proyecto (sobre todo de cara a publicarse) va a necesitar mantenimiento ocasional para seguir compatible con nuevas versiones de RetroBat.

9. **Seguridad física del kiosko**: nada de esto evita que un cliente con acceso físico a un teclado conectado al cabinet pueda, en teoría, cerrar `monedero.exe` desde el Administrador de tareas si el modo kiosko de Windows no está bien bloqueado. Esto está fuera del alcance del programa en sí, pero vale la pena tenerlo presente como parte de la configuración general del kiosko (deshabilitar Ctrl+Alt+Supr, Administrador de tareas, etc., a nivel de Windows).

## 11. Consideraciones de diseño para publicación open source

- **Genérico desde el diseño**: nada de rutas absolutas ni listas de juegos hardcodeadas; el sistema y las ROMs deben detectarse dinámicamente desde la estructura de RetroBat. Favoritos puede ser un mecanismo opcional, nunca una dependencia obligatoria.
- **Licencia**: a definir (sugerencia: MIT por simplicidad, o GPL si se quiere forzar que los forks también sean libres).
- **Documentación**: README con requisitos (versión de RetroBat, .NET runtime), instalación, cómo se genera/edita el .ini, hotkeys por emulador soportado y cómo remapearlas.
- **Empaquetado**: instalador simple o .zip con el .exe + un .ini de ejemplo comentado.
- **Nombre del proyecto**: pendiente de definir, útil para el repo y la documentación.

## 12. Estado actual y próximos pasos

Hasta el momento se definió el diseño conceptual completo (arquitectura, mecanismos de pausa por grupo de emuladores, formato de configuración, generación automática, mejoras visuales futuras, y consideraciones de publicación). **Aún no se escribió código.**

### Fase 1 — MVP indispensable (prioridad: eficiencia, simpleza, cero conflictos)

El objetivo de esta fase es tener el ciclo básico funcionando de forma 100% confiable, sin errores ni conflictos con el emulador o con RetroBat: detectar el juego → contar tiempo → mostrar aviso simple de "insertar crédito" + countdown → pausar mientras tanto → reanudar si hay crédito, o cerrar el emulador si se agota el tiempo → repetir en loop.
1. Detector de procesos + construcción de `GameSession`, sin asumir una correspondencia rígida entre juego y PID.
2. Clasificación por **sistema RetroBat** (lista blanca de sistemas exentos); Favoritos queda como mecanismo opcional y no como dependencia.
3. Implementar `config.ini` con defaults por sistema y `.ini` por ROM usando `modo=heredar|forzar|desactivar`.
4. Implementar la **máquina de estados de sesión** (`IDLE`, `RUNNING`, `WAITING_CREDIT`, `CLOSING`) como centro de la lógica.
5. Integración de pausa por red con RetroArch (`GET_STATUS`, `PAUSE_TOGGLE`, `QUIT`) como primer caso de prueba.
6. Overlay mínimo funcional: texto simple ("INSERT COIN"), fuente de sistema básica, countdown visible, sin skins ni escalado sofisticado.
7. Lógica temporal basada en `Stopwatch`/tiempo monotónico para evitar drift.
8. Countdown + salida automática del emulador al agotarse el tiempo sin crédito.
9. Loop de reinserción de crédito mediante `duracion_credito`.
10. Hook de teclado para el input físico del monedero (IPAC/JPAC), con callback mínimo y procesamiento del pulso fuera del hook.
11. Extender la integración de pausa/salida a los emuladores standalone (Dolphin, PCSX2, DuckStation) mediante una interfaz común y simulación de hotkeys.
12. Implementar una **escalera de cierre limpio** con margen de espera antes de recurrir a `Process.Kill()`.
13. Implementar un mecanismo simple de watchdog separado del flujo principal.
14. Soportar `pulsos_por_moneda` / normalización de pulsos y mantenerlo separado de `monedas_por_credito`.
15. Incorporar un **modo de prueba** con tiempos cortos, crédito virtual y logging detallado, sin `Kill()` forzado.
16. Probar exhaustivamente en la PC real del bar, incluyendo fullscreen exclusivo, cierre inesperado, crédito durante transición y recuperación ante caída del programa.

### Fase 2 — Mejoras (recién después de validar que la Fase 1 es 100% estable)

1. Fast-forward automatizado durante intros largas (opcional por juego/sistema), desacoplado del núcleo.
2. Sistema de múltiples skins visuales para el overlay.
3. Adaptación visual fina por resolución/aspect ratio (canvas virtual + escalado, tabla de resoluciones conocidas — ver sección 8).
4. **Interfaz de configuración oculta** (ver sección 13).
5. Herramientas de diagnóstico/configuración avanzada apoyadas en el mismo motor de configuración.
6. Documentación y empaquetado para publicación open source (README, licencia, instalador).

## 13. Interfaz de configuración oculta (Fase 2)

Capa de conveniencia sobre el mismo .ini, que **convive con la edición manual** (el .ini sigue siendo la fuente de verdad y se puede seguir editando con cualquier editor de texto). Se activa con una tecla/combinación desde el cabinet, sin salir del modo kiosko.

**Funciones:**
- Configuración general del loop: valores globales por defecto (`monedas_por_credito`, `tiempo_inicial`, `duracion_credito`, `countdown`) aplicables salvo override por juego. Los campos de tiempo se editan siempre en minutos/segundos; la interfaz realiza la conversión interna automáticamente.
- **Pestañas por sistema, creadas bajo demanda**: un botón "+" / "agregar sistema" permite crear la pestaña de un sistema (ej. `psx`), usando el **nombre exacto con que RetroBat identifica ese sistema**, no el del emulador. Dentro de la pestaña se configuran los defaults del monedero para todo ese sistema, que luego heredan todas sus ROMs (con posibilidad de override individual por juego).
- **Escaneo por carpeta de ROMs**: pre-genera las secciones del .ini para todos los ROMs de un sistema en una sola pasada, sin necesidad de ejecutar cada juego para que se cree su entrada.
- **Independencia de la lista de favoritos**: al agrupar por sistema y permitir escanear la carpeta de ROMs directamente, el programa funciona igual para usuarios que no usen una colección de favoritos — basta con agregar la pestaña del sistema y escanear su carpeta para tener el monedero configurado en todo ese sistema.
- **Edición contextual del juego en curso**: como el programa ya sabe en todo momento qué juego está corriendo (mismo dato usado para el sistema de crédito), al abrir la interfaz con un juego activo aparece un botón tipo "editar configuración de este juego", sin necesidad de buscarlo manualmente. Dos formas de editar, ambas válidas:
  - Abrir el archivo .ini real en el editor de texto por defecto de Windows, en primer plano.
  - Un formulario dentro de la misma interfaz con los campos ya cargados (`monedas_por_credito`, `tiempo_inicial`, `countdown`, `duracion_credito`, etc.), para editar sin salir del programa.
  Al guardar desde cualquiera de las dos formas, el programa establece automáticamente `modo=forzar` en el `.ini` del juego. Para volver a los defaults del sistema se debe poder seleccionar explícitamente `modo=heredar`; para excluir el juego, `modo=desactivar`.

**Consideraciones de diseño:**
- **Tecla de activación**: en un cabinet de bar en modo kiosko, conviene evitar teclas simples tipo F5 (un cliente con acceso a un teclado podría abrir la config y regalarse créditos). Preferible una combinación poco probable de pulsar por accidente (ej. Ctrl+Alt+F5) e idealmente protegida con un PIN simple configurable.
- **Un solo proceso**: la interfaz debe ser una ventana del mismo ejecutable que corre en segundo plano, no un programa aparte — así el acceso de lectura/escritura al .ini queda centralizado en un único proceso y se evitan lecturas parciales o cambios pisados por concurrencia (si fueran dos ejecutables, habría que implementar locking, complejidad innecesaria).
- **Edición quirúrgica**: al guardar desde la interfaz, modificar solo las claves tocadas en vez de regenerar el archivo completo, para no perder comentarios ni secciones que el usuario haya escrito manualmente.
