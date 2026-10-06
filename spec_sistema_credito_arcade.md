# Especificación técnica: Sistema de crédito virtual para PC Arcade (RetroBat)

## 1. Contexto y objetivo

Se está armando una **PC arcade para un bar**, corriendo **RetroBat** (fork de EmulationStation) sobre **Windows 10**, en modo kiosko, con una única lista de favoritos que mezcla juegos de MAME/arcade y juegos de consola/PC emulados.

**Problema a resolver:** los juegos de MAME/arcade respetan naturalmente la lógica de "monedero" (partidas finitas, se pierde y hay que reinsertar crédito). Los juegos de consola o PC no tienen ese límite natural: un cliente puede jugar una partida interminable (ej. un RPG o un Mario Kart) mientras otros clientes del bar esperan su turno sin poder usar la máquina.

**Objetivo del programa:** crear una herramienta que corra en segundo plano en Windows y **le imponga artificialmente la lógica de monedero a juegos que no la tienen de forma nativa**, interrumpiendo la partida con un overlay de "insertar crédito" después de un tiempo configurable, y repitiendo esa interrupción cada cierto intervalo mientras el juego siga activo. El objetivo final es que el cliente deba comprar más crédito o resignar la partida, liberando la máquina para otros — sin importar si juega el jugador 1 o el jugador 2 en modo multiplayer local (no hace falta diferenciarlos).

**Meta adicional:** el programa está pensado para eventualmente publicarse como **proyecto open source**, funcional y terminado, para que otros usuarios de RetroBat con el mismo problema (cabinets de bar/local comercial) puedan usarlo. Esto implica que el diseño debe ser genérico desde el principio (nada hardcodeado a la configuración de un usuario particular), bien documentado, y con una licencia definida (a decidir; MIT es la sugerencia por defecto por simplicidad).

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

3. **Configuración por juego (.ini/.txt)**: cada juego no-arcade tiene una sección con parámetros configurables (ver sección 5). Si un juego se lanza por primera vez y no tiene sección propia, el programa **genera automáticamente una entrada con valores por defecto**, que el usuario puede editar después a mano.

4. **Integración de pausa/control por emulador** (ver sección 4): pausa la emulación al mostrar el overlay, y la reanuda al insertar crédito, sin pérdida de progreso (no es un save state, es pausa real).

5. **Overlay**: ventana sin bordes, topmost, semitransparente, mostrando el mensaje de "insertar crédito" (tipo "INSERT COIN TO CONTINUE"). Debe soportar **múltiples skins** seleccionables (carpetas con imagen de fondo + config de fuente/color, referenciadas desde el .ini).

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

## 5. Formato del archivo de configuración (.ini)

Estructura propuesta (ejemplo ilustrativo, a refinar):

```ini
[dolphin.exe|MarioKartWii]
tiempo_overlay=40
intervalo_loop=1200
fast_forward=true
tiempo_fast_forward=38
skin=default

[retroarch.exe|SonicAdventure2]
tiempo_overlay=20
intervalo_loop=1200
fast_forward=false
skin=neon_arcade
```

- Cada sección identifica el proceso del emulador y, cuando aplica, el juego específico.
- `tiempo_overlay`: segundos desde el lanzamiento hasta que aparece el primer overlay de "insertar crédito".
- `intervalo_loop`: segundos entre reapariciones del overlay tras insertar crédito.
- `fast_forward` / `tiempo_fast_forward`: opcionales, solo si el sistema lo soporta.
- `skin`: referencia a la carpeta/tema visual del overlay para ese juego.
- Si un juego no tiene sección al lanzarse por primera vez, el programa la **genera automáticamente con valores por defecto**, editable después a mano.

## 6. Consideraciones de diseño para publicación open source

- **Genérico desde el diseño**: nada de rutas absolutas ni listas de juegos hardcodeadas; todo debe detectarse dinámicamente desde los favoritos de RetroBat de cada usuario.
- **Licencia**: a definir (sugerencia: MIT por simplicidad, o GPL si se quiere forzar que los forks también sean libres).
- **Documentación**: README con requisitos (versión de RetroBat, .NET runtime), instalación, cómo se genera/edita el .ini, hotkeys por emulador soportado y cómo remapearlas.
- **Empaquetado**: instalador simple o .zip con el .exe + un .ini de ejemplo comentado.
- **Nombre del proyecto**: pendiente de definir, útil para el repo y la documentación.

## 7. Estado actual y próximos pasos

Hasta el momento se definió el diseño conceptual completo (arquitectura, mecanismos de pausa por grupo de emuladores, formato de configuración, generación automática, skins, y consideraciones de publicación). **Aún no se escribió código.**

Próximos pasos posibles (a decidir con quien retome el proyecto):
1. Armar el prototipo base: detector de procesos + lectura/generación del .ini + clasificación arcade/no-arcade vía favoritos de RetroBat.
2. Implementar la integración de pausa/fast-forward por red con RetroArch como primer caso de prueba (es el más simple y confiable).
3. Extender a los emuladores standalone (Dolphin, PCSX2, DuckStation) con simulación de hotkeys.
4. Diseñar el overlay (layout visual, información mostrada, sistema de skins).
5. Integrar el hook de teclado para el input físico del monedero (tipo IPAC/JPAC).
6. Probar en la PC real del bar, ajustando tiempos por juego en el .ini.
7. Preparar documentación y empaquetado para publicación open source.
