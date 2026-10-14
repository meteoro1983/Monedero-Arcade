# Monedero Arcade

**Sistema de crédito virtual para RetroBat en Windows.** Pensado para una PC arcade de bar con una sola lista de juegos
que mezcla arcade (MAME, FBNeo…) con consolas y juegos de PC.

Los juegos arcade ya piden fichas solos. Los de consola o PC no, y un cliente puede jugar sin fin mientras otros esperan.
El monedero corre en segundo plano y, cuando detecta un juego que no es arcade:

1. lo deja jugar un tiempo configurable;
2. **pausa el juego** y muestra **INSERT COIN** con una cuenta regresiva;
3. si alguien mete crédito (moneda o botón), reanuda y vuelve a avisar cada N minutos;
4. si nadie pone crédito, muestra **GAME OVER**, **cierra el emulador limpio** y vuelve al menú de RetroBat.

El crédito es de la máquina: da igual si juega el jugador 1 o el 2.

Estado: **beta 3.10**, probada en una arcade real con CRT (CRT EmuDriver, resoluciones de 240p y súper resoluciones).

---

## Qué soporta

| Grupo | Emuladores / sistemas | Cómo pausa | Cómo cierra |
|---|---|---|---|
| RetroArch | NES, SNES, Mega Drive, PC Engine, N64, PS1, Dreamcast (Flycast), PSP (core) | comandos de red (`PAUSE_TOGGLE` verificado con `GET_STATUS`, nunca a ciegas) | `QUIT` → cierre de ventana → `Kill` como último recurso |
| Standalone | Dolphin, PCSX2, DuckStation, PPSSPP, Redream | **suspensión del proceso** (CPU a 0, sin depender de hotkeys) o hotkey | `WM_CLOSE` a todas sus ventanas (RetroBat lo ve como salida normal) |
| Juegos de PC | sistema `windows` de RetroBat (ports, juegos nativos) | suspende todo el árbol de procesos, lo minimiza y silencia su audio | `WM_CLOSE` al árbol, espera y termina lo que quede |
| Arcade (exentos) | `mame, neogeo, fbneo, fb, naomi, atomiswave, model2, model3, teknoparrot` | no se tocan | — |

Probado con RetroBat: RetroArch, DuckStation, Dolphin, PCSX2, PPSSPP (standalone y core) y juegos de PC (Starship,
ports varios).

## Funciones

- **Cartel retro en el CRT**: se dibuja con alto entero de líneas (224/240/256, ×2 en 448/480, ×3 en 768) y se
  rasteriza al ancho real del modo (2560, 3520, 5376…) suponiendo un tubo 4:3. No se deforma en súper resoluciones.
  Fuentes TTF (Press Start 2P, Retro Gaming) y sonido de moneda al acreditar.
- **Tipos de aviso** por sistema o por juego (`aviso=`):
  - `overlay`: cartel encima del juego.
  - `osd`: mensaje dentro de RetroArch.
  - `ambos`: los dos (útil en N64/OpenGL, que puede tapar ventanas).
  - `banner`: franja que baja desde arriba sin minimizar ni quitarle el foco al juego.
- **Crédito**: tecla de teclado (el encoder del monedero físico, por defecto `5`) o botón de un joystick XInput
  (por defecto Select). `pulsos_por_credito` sirve para monederos que mandan varios pulsos por moneda.
- **Salida universal Select+Start** (juegos de PC): manteniendo el combo medio segundo se cierra el juego y se vuelve a
  RetroBat. Restaura el modo de video del escritorio y devuelve el foco a EmulationStation. También funciona con
  juegos que se lanzan desde un launcher intermedio.
- **Fast-forward automático** de intros largas (RetroArch).
- **Configuración por capas**: `config.ini` con valores por defecto por sistema, y un `.ini` por juego (se genera solo
  con `activo=0`; con `activo=1` pisa al del sistema campo por campo). `exento=1` en el `.ini` de un juego lo saca del
  sistema de crédito (juegos con fichas propias).
- **Red de seguridad**: los procesos suspendidos se anotan en `suspendidos.txt` y se reanudan solos si el monedero se
  cae o se reinicia.
- **MonederoConfig.exe**: configurador gráfico aparte, pensado para 640x480. Pestañas:
  - *General*: tecla de crédito, botón del joystick, sonido, fuente, textos, PIN.
  - *Sistemas*: valores por defecto de cada sistema.
  - *Emuladores*: método de pausa de cada uno.
  - *Juegos*: los `.ini` por juego, con escaneo de la carpeta de ROMs.
  - *Vista previa*: el cartel tal cual se ve en cada modo del CRT.

  El monedero relee los cambios sin reiniciarse. También se abre con `Ctrl+Alt+F5` o con el ícono de la bandeja.

## Instalación

1. Bajá la versión portable desde **Releases**, o compilá (abajo).
2. Copiá `monedero.exe` y `MonederoConfig.exe` en una carpeta, por ejemplo `RetroBat\monedero\`.
3. En `retroarch.cfg` activá los comandos de red: `network_cmd_enable = "true"` (puerto 55355).
4. Hacé que `monedero.exe` arranque con Windows (acceso directo en *Inicio*, o tarea programada).
5. La primera vez genera `config.ini`. Ajustalo con `MonederoConfig.exe` o a mano.

Archivos que genera junto al exe: `config.ini`, `monedero.log`, `suspendidos.txt` (solo si hay algo suspendido) y una
carpeta por sistema con los `.ini` de cada juego (nunca escribe en la carpeta de ROMs).

## Configuración (extracto de `config.ini`)

```ini
[general]
fuente=PressStart2P.ttf
sonido_credito=coin-sound.mp3
sistemas_exentos=mame,neogeo,fbneo,fb,naomi,atomiswave,model2,model3,teknoparrot
sistemas_pc=windows
salida_combo=BACK+START
salida_ms=500
;pin_config=1234

[default]
tiempo_overlay=20        ; segundos de juego antes del primer aviso
intervalo_loop=1200      ; cada cuánto vuelve a pedir crédito (s)
tiempo_countdown=20      ; cuenta regresiva del INSERT COIN (s)
credit_key=D5            ; tecla del monedero (nombres de System.Windows.Forms.Keys)
credit_pad=BACK          ; botón XInput
pulsos_por_credito=1
fast_forward=0

[n64]
aviso=ambos

[windows]                ; juegos de PC
tiempo_overlay=60

[emulador:dolphin]
metodo_pausa=suspend     ; o hotkey + tecla_pausa=F10
minimizar_al_pausar=1    ; Dolphin usa pantalla completa exclusiva y taparía el cartel

[emulador:pc]
silenciar_al_pausar=1
espera_cierre_ms=4000
```

Los nombres de sistema son los de las carpetas de `roms\` de RetroBat. La configuración se ata al **sistema**, no al
emulador: si cambiás de emulador para PS1, la sección `[psx]` sigue valiendo.

## Compilar

Requiere el SDK de .NET 8. En `MonederoArcade\` y en `MonederoConfig\`:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o salida
```

C# / .NET 8 / WinForms. Única dependencia NuGet: `System.Management`, que se usa para leer con qué juego lanzó
RetroBat al emulador.

## Estructura

| Archivo | Qué hace |
|---|---|
| `Program.cs` | entrada, ícono de bandeja, sondeo de procesos, sesiones |
| `ProcessWatcher.cs` / `ProcessTree.cs` | detecta emulador, sistema y juego (por la línea de comandos que usa RetroBat) |
| `GameSession.cs` | máquina de estados de la partida (aviso → pausa → cuenta regresiva → crédito o cierre) |
| `ControlEmulador.cs` / `ProcessControl.cs` / `RetroArchClient.cs` | pausa, reanudación y cierre por grupo |
| `OverlayForm.cs` / `OverlayRenderer.cs` | cartel INSERT COIN / GAME OVER / banner, escalado para CRT |
| `CreditHook.cs` / `GamepadCredit.cs` | crédito por teclado (hook global) y XInput |
| `SalidaUniversal.cs` | combo Select+Start para juegos de PC |
| `AudioMute.cs` / `Sonido.cs` | silenciar el juego pausado / sonido de moneda |
| `GameConfig.cs` / `IniFile.cs` | configuración por capas, edición del `.ini` sin perder comentarios |
| `MonederoConfig/` | configurador gráfico |

Diseño completo y decisiones: `spec_sistema_credito_arcade_actualizada.md` y `monedero_resumen_detallado.md`.
Notas de cada versión: `MonederoArcade/README.md`.

## Limitaciones conocidas

- El botón de crédito del joystick solo funciona con controles **XInput**. El teclado funciona siempre.
- Los juegos en pantalla completa exclusiva pueden tapar el cartel. Para eso están `minimizar_al_pausar` y `aviso=banner`.
- Matar un emulador puede dañar memory cards o partidas guardadas. Por eso el cierre es escalonado y `Kill` es el
  último recurso.
- La seguridad física del kiosko (Administrador de tareas, Ctrl+Alt+Supr) queda fuera del monedero.

## Licencia

MIT. Proyecto de la comunidad, sin relación con RetroBat ni RetroArch.
