# Monedero Arcade — Resumen detallado del proyecto (para continuar en Claude Code)

> Este documento resume TODO lo decidido y probado hasta la beta 3.5 en una conversación larga con Claude (versión web, sin acceso a la PC). Claude Code no tiene acceso a esa conversación, así que acá está el contexto completo. El usuario habla español (rioplatense, informal); responder en español.

## 1. Qué es y por qué existe

PC arcade para un **bar**, con **RetroBat (EmulationStation) en Windows 10**, modo kiosko, una lista única de favoritos que mezcla juegos arcade (MAME y afines) y juegos de consola/PC.

**Problema:** los arcade respetan el monedero de forma natural (partidas finitas). Los juegos de consola/PC no: un cliente puede jugar una partida interminable mientras otros esperan.

**Solución (este programa, "monedero"):** proceso en segundo plano que detecta cuándo corre un emulador con un juego **no arcade**, y tras un tiempo configurable **pausa el juego, muestra "INSERT COIN" con un countdown**. Si se inserta crédito → reanuda y repite el aviso cada N minutos (loop). Si el countdown llega a 0 sin crédito → **cierra el emulador y vuelve limpiamente al menú de RetroBat**. Da igual si juega el jugador 1 o el 2: el crédito es de la máquina.

**Meta a futuro:** publicarlo como **open source** para la comunidad de RetroBat, "terminado y funcional". Por eso: nada hardcodeado al setup del usuario, configurable por .ini, bien documentado.

## 2. Prioridades y preferencias del usuario (importante)

1. **Primero estabilidad, eficiencia y simpleza operativa.** Que no tire errores ni genere conflictos al superponerse con el emulador o con RetroBat. "100% eficiente" antes de cualquier mejora.
2. Texto del overlay **simple y retro**, nada sofisticado. Skins y estética fina quedan para después de que el núcleo sea estable.
3. Configurable por .ini, con edición manual siempre posible; la interfaz gráfica es una comodidad, no un reemplazo.
4. Quiere que el programa se adapte a otros setups (otras resoluciones, otros juegos), sin "locuras" ni comportamiento inconsistente.
5. Prefiere honestidad sobre lo que no se probó. Reporta resultados de pruebas reales en su arcade y luego se itera.

## 3. Hardware / entorno del usuario

- PC arcade vieja pero suficiente, **Windows 10**, con **CRT-emudrivers (CRT EmuDriver)**: cambia la resolución real del sistema al lanzar cada juego.
- Resoluciones que aparecen: 224/240p nativo (320x240, 384x224), **super-resolución 2560x240** (ancho, bajo; sigue siendo pantalla 4:3 física), 1080x240p vía HDMI, y el **core de N64 usa resolución entrelazada** similar a la consola original. Juegos de PC nativos: HD/4K normal.
- Monedero físico vía encoder tipo IPAC/JPAC que emula una **tecla de teclado** (por defecto "5", la de MAME para insertar moneda del jugador 1). También se pidió **Select de un control XInput** como crédito.
- Sistemas/emuladores que usa o piensa usar:
  - **RetroArch (Grupo A)**: NES, SNES, Mega Drive, PC Engine, N64 (core mupen/parallel — verificar cuál), Dreamcast (**Flycast** dentro de RetroArch, en vez de Redream), PS1 (probable **SwanStation**).
  - **Standalone (Grupo B)**: Dolphin (GameCube/Wii), PCSX2 (PS2), DuckStation (PS1, como respaldo), posiblemente Redream.
  - Juegos de PC nativos (sin mecanismo de pausa estándar; aún no soportados).
- **Sistemas arcade exentos** (el programa no los toca): `mame, neogeo, fbneo, fb, naomi, atomiswave, model2, model3, teknoparrot`. (Capcom suele correr por FBNeo cuando MAME no los soporta; se clasifica por carpeta de sistema, no por publisher.) A veces usa MESS (mismo ejecutable que MAME) para consolas: por eso existe el flag `activo`.

## 4. Diseño funcional acordado

### 4.1 Ciclo de una partida (máquina de estados)
`lanzamiento → [fast-forward opcional] → espera tiempo_overlay → overlay + pausa + countdown (tiempo_countdown, por defecto 20 s) → crédito: reanudar y esperar intervalo_loop (por defecto 20 min) / sin crédito: cerrar emulador → RetroBat`.

### 4.2 Jerarquía de configuración
- `config.ini` (junto al .exe): una sección **por sistema** con los defaults (`[dreamcast]`, `[psx]`, `[wii]`, …) más `[default]`. Nombres de sistema **tal cual los nombra RetroBat** (carpetas de `roms\`). La config se ata al **sistema**, no al emulador (si cambia SwanStation por DuckStation, `psx` sigue valiendo).
- `<sistema>\<archivo-rom>.ini` (carpeta `monedero\<sistema>\`, **nunca en la carpeta de ROMs**): un .ini por ROM, nombre = nombre del archivo con extensión + `.ini`. Se **genera automáticamente** (al primer lanzamiento o al escanear la carpeta) con **`activo=0`**: mientras sea 0 manda el default del sistema. Con `activo=1` sus valores pisan al default (campo por campo).
- Defaults de tiempo de primer aviso por generación: cartucho (NES/SNES/MD/PCE) 5–10 s; PS1 en adelante ~20 s; Wii/GameCube/PS2 más (25 s) y algunos juegos (ej. Mario Kart Wii) necesitan mucho más → override por ROM.
- La clave de identidad de un juego es **sistema + nombre de archivo real** (no el ID/serial interno del emulador, que cambia entre regiones). Se obtiene de la **línea de comandos del proceso** (WMI `Win32_Process.CommandLine`) con que RetroBat lanzó el emulador. Generación idempotente (no duplica; respeta .ini creados a mano).

### 4.3 Mecanismos de pausa y cierre por grupo
- **RetroArch**: Network Command Interface por UDP (127.0.0.1:55355), requiere `network_cmd_enable = "true"` en `retroarch.cfg`. Comandos usados: `PAUSE_TOGGLE`, `GET_STATUS` (para no desincronizar el toggle), `QUIT`, `FAST_FORWARD` (toggle), `SHOW_MSG` (OSD nativo; **no verificado** que exista en su versión).
- **Standalone**: por defecto **suspensión del proceso** (`NtSuspendProcess`/`NtResumeProcess`), sin necesitar hotkeys; alternativa `metodo_pausa=hotkey` (simula tecla con `keybd_event` tras `SetForegroundWindow`). PIDs suspendidos se guardan en `suspendidos.txt` para recuperarlos si el programa cae.
- **Cierre**: escalonado para que RetroBat lo vea como salida normal: RetroArch → `QUIT` (x2, por si hay `quit_press_twice`) → `CloseMainWindow` → `Kill` solo como último recurso. Standalone → reanudar si estaba suspendido → `CloseMainWindow` → `Kill`. Durante el cierre el overlay pasa a "GAME OVER" y sigue tapando la pantalla (para que no se vea volver al juego).

### 4.4 Overlay
Ventana sin bordes, topmost, no-activa (`WS_EX_NOACTIVATE|TOOLWINDOW|TOPMOST`), se **reposiciona al mostrarse** sobre la pantalla donde está la ventana del emulador (la resolución cambia después del lanzamiento con CRT EmuDriver). Dibuja en un **canvas virtual** (240 px de alto; ancho 4:3 si la pantalla es baja <300px, o aspecto real si no) y lo escala con **nearest-neighbor** → no se deforma en 2560x240. Idea de referencia: el RGUI de RetroArch (canvas fijo + escalado). Modo DPI PerMonitorV2. Se re-fuerza topmost cada segundo.
Pendiente (Fase 2): tabla de resoluciones conocidas → aspect ratio, y fuente vectorial en resoluciones muy altas (>1440p) para no verse "bloque".

### 4.5 Crédito
- Teclado: hook global `WH_KEYBOARD_LL` siempre instalado pero "armado" solo con overlay visible (callback liviano; posta al hilo de UI). Tecla configurable (`credit_key`, nombres de `System.Windows.Forms.Keys`, ej. `D5`).
- XInput: polling 50 ms mientras el overlay está activo, botón `credit_pad` (por defecto `BACK` = Select, máscara 0x0020). Solo funciona con controles XInput.
- `pulsos_por_credito`: cuenta N pulsos dentro de una ventana de 2 s (monederos que mandan varios pulsos por moneda).

### 4.6 Interfaz de configuración (Fase 2, ya implementada en 3.5)
`Ctrl+Alt+F5` (o clic derecho en el ícono de bandeja): pestaña *Sistemas* (tabla editable de defaults, agregar sistema, escanear carpeta de ROMs → crea .ini con `activo=0`) y pestaña *Juego actual* (edita el juego en curso; guardar lo deja `activo=1` y aplica de inmediato). PIN opcional (`[general] pin_config=`). Misma ejecutable/proceso (sin conflictos de escritura del .ini). Edición "quirúrgica" del .ini (no reescribe todo, conserva comentarios).

### 4.7 Otras ideas acordadas / pendientes
- Fast-forward automático al inicio (solo RetroArch) para intros largas: `fast_forward`, `tiempo_fast_forward`; termina antes del primer aviso y se apaga antes de pausar.
- Skins del overlay (Fase 2/3), sin prioridad.
- Watchdog como proceso aparte (hoy solo red de seguridad por excepciones + recuperación de suspendidos al inicio).
- Juegos de PC nativos (idea: overlay + suspensión de proceso o bloqueo de input).
- Lectura de favoritos/gamelist de RetroBat para clasificar (hoy se clasifica solo por carpeta de sistema).
- Riesgos conocidos: overlay sobre **fullscreen exclusivo**; corrupción de memory cards/saves al matar procesos; scripts post-juego de RetroBat; compatibilidad futura con cambios de RetroBat; seguridad física del kiosko (Task Manager, Ctrl+Alt+Supr).

## 5. Código actual (beta 3.5) — C# / .NET 8 / WinForms

Proyecto: `MonederoArcade.csproj` (net8.0-windows, WinExe, `AssemblyName=monedero`, `ApplicationHighDpiMode=PerMonitorV2`, único paquete NuGet: `System.Management`). Compilar:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o salida
```
(El código fue escrito sin poder compilar ni probar en el entorno de Claude web: **puede haber errores de compilación o de comportamiento**.)

| Archivo | Responsabilidad |
|---|---|
| `Program.cs` | Entrada, ícono de bandeja, polling de procesos (1.5 s), crea/gestiona `GameSession`, abre `ConfigForm`, handlers de excepciones (reanudar suspendidos), `UiThread.Init()` |
| `ProcessWatcher.cs` | Detecta procesos por prefijo (`retroarch, dolphin, pcsx2, duckstation, redream`), lee la línea de comandos por WMI, extrae ruta de contenido (último token con extensión, sin `-`/`=`, prefiere rutas con separador) y deriva el sistema (carpeta tras `roms\`, o carpeta contenedora) |
| `GameSession.cs` | Máquina de estados por partida (fast-forward, overlay, pausa, countdown, crédito/pulsos, loop, cierre con "GAME OVER") |
| `ControlEmulador.cs` | Abstrae Pausar/Reanudar/Cerrar/FastForward/ShowMsg por emulador (RetroArch UDP; standalone suspend o hotkey); `Cerrar()` escalonado |
| `RetroArchClient.cs` | UDP a RetroArch: `Send`, `Query` (espera respuesta), `EstaPausado` (parsea `GET_STATUS PAUSED/PLAYING`), `PausarSiHaceFalta` (verifica y reintenta), `ReanudarSiHaceFalta`, `Quit`, `FastForward`, `ShowMsg` |
| `ProcessControl.cs` | Suspender/Reanudar procesos + persistencia en `suspendidos.txt` + recuperación al inicio |
| `OverlayForm.cs` | Overlay topmost no-activo con canvas virtual y modo "GAME OVER" |
| `CreditHook.cs` | Hook global de teclado: crédito (armado) y chord `Ctrl+Alt+F5` → `ConfigSolicitada` |
| `GamepadCredit.cs` | Polling XInput (`xinput1_4.dll`) del botón de crédito (flanco de subida, snapshot al armar) |
| `GameConfig.cs` | `GameConfig` + `ConfigManager`: genera `config.ini` por defecto, resuelve config por sistema/ROM (`activo`), plantillas, escaneo de carpetas, lista/guarda sistemas, `ResolveEmulador` |
| `IniFile.cs` | Parser/escritor .ini minimalista con edición quirúrgica y `Sections()` |
| `ConfigForm.cs` | Interfaz (tabs Sistemas / Juego actual) y `PinPrompt` |
| `UiThread.cs` | Postear acciones al hilo de UI desde cualquier hilo (ancla `Control`) |
| `NativeMethods.cs` | P/Invoke (OpenProcess, NtSuspend/ResumeProcess, SetForegroundWindow, keybd_event, MapVirtualKey) |
| `Logger.cs` | `monedero.log` junto al .exe (línea de comandos de cada emulador, resolución del overlay, pausa verificada, vía de cierre, etc.) |

Archivos que genera junto al .exe: `config.ini`, `monedero.log`, `suspendidos.txt` (solo si hay algo suspendido), carpetas `<sistema>\*.ini`.

Hay además un documento de especificación anterior (`spec_sistema_credito_arcade.md`) con el diseño conceptual; **donde difiera, manda este resumen** (ej.: pausa por suspensión en standalone, interfaz ya implementada, overlay con canvas virtual, ini por ROM con `activo=0`).

## 6. Historial de pruebas reales (reportadas por el usuario)

- **Beta 0.1** (solo RetroArch): funciona el ciclo básico. En NES/Genesis/SNES a 2560x240 aparecía "insert coin" pero el overlay ocupaba **media pantalla**, **sin contador visible**, y luego cerraba el emulador. **N64**: no se veía el overlay (resolución entrelazada). Al cerrar, RetroBat decía "el emulador se cerró inesperadamente" (cierre tipo kill, código de salida anormal).
- **Beta 0.2**: overlay **legible en super-resoluciones**. **N64**: sigue sin overlay y **no pausa**. Countdown "pasa muy rápido" (causa: defaults de 15 s en `config.ini` → deben ser 20 s). Al terminar el countdown el juego **volvía a verse unos segundos y luego se cerraba**: demasiado largo; ya no aparece el cartel de cierre inesperado.
- **Beta 0.3**: no la probó (saltó a la 3.5).
- **Beta 3.5**: el usuario dice que **todavía tiene problemas** (los detallará; pedirle el `monedero.log` y la descripción exacta). Su plan: conectar el disco de la PC arcade y pedirle a Claude Code que haga **prueba y error con acceso a los archivos de RetroBat**.

## 7. Hipótesis abiertas (verificar con acceso a la PC)

1. **N64 sin pausa ni overlay** — dos candidatos: (a) la sesión ni siquiera se crea (la línea de comandos que arma RetroBat para ese core no se parsea bien: revisar `monedero.log`, línea `retroarch pid=... cmdline=...`); (b) el core corre en **pantalla completa exclusiva** (necesario a veces para que CRT EmuDriver/switchres cambie de modo) y una ventana externa no se dibuja encima. Mitigaciones previstas: `video_windowed_fullscreen`, `osd_retroarch=1` (SHOW_MSG), o dibujar el aviso dentro de RetroArch.
2. **RetroBat regenera `retroarch.cfg`** (o aplica overrides por core/sistema/juego) al lanzar: podría estar poniendo `network_cmd_enable=false` o `quit_press_twice`, o cambiando driver de video, **solo para algunos cores** (explicaría que NES/SNES/MD funcionen y N64 no). Revisar `retroarch.cfg`, `config\<core>\*.cfg` y `config\*.cfg` de overrides, y cómo los genera RetroBat.
3. **RetroBat lanza los emuladores con un launcher propio** (se cree que `emulatorLauncher.exe`, de código abierto en GitHub: *RetroBat-Official/emulatorlauncher*, a verificar en `es_systems.cfg`). Ese launcher arma la línea de comandos, genera configs y **espera al emulador**; si el código de salida es distinto de 0 muestra el error de "cierre inesperado". Estudiarlo sirve para: ver qué argumentos reales recibe cada emulador, cómo detecta cierres, y si hay una vía "oficial" de salida limpia a la cual sumarse.
4. **`QUIT` por red tardaba/no cerraba al primer intento** en RetroArch (visto en las pruebas): sospecha de `quit_press_twice` o confirmaciones. La 3.5 manda QUIT dos veces; verificar con el log qué vía de cierre se usó (`QUIT (1)`, `QUIT (2)`, `CloseMainWindow` o `Kill`).
5. **Pausa por suspensión en standalone**: probable zumbido/loop de audio mientras está suspendido y "ventana no responde" (ghost window) de Windows; sin verificar con Dolphin/PCSX2/DuckStation reales. Alternativa: `metodo_pausa=hotkey` (las hotkeys por defecto puestas en `config.ini` para PCSX2/DuckStation/Redream son **suposiciones**, hay que leerlas de cada emulador).
6. **Comandos de RetroArch no verificados**: formato exacto de la respuesta de `GET_STATUS`, existencia de `SHOW_MSG`, semántica de `FAST_FORWARD` (toggle), puerto 55355. Con acceso a la PC se pueden probar a mano (ej. `echo GET_STATUS | ncat -u 127.0.0.1 55355` o PowerShell con `UdpClient`).
7. Hook de teclado: con Alt presionado F5 llega como `WM_SYSKEYDOWN` (ya contemplado). Verificar que el chord `Ctrl+Alt+F5` funcione y no choque con atajos de RetroArch/RetroBat.

## 8. Cómo continuar con Claude Code (sugerencias)

1. Extraer el proyecto (zip de la beta 3.5) en una carpeta de trabajo **fuera** de `RetroBat\` y compilar; corregir errores de compilación si los hay.
2. **Hacer copia de seguridad** de `retroarch.cfg`, `es_systems.cfg`, `es_settings.cfg` y configs de emuladores antes de tocar nada de RetroBat. Proponer los cambios al usuario antes de modificar archivos del arcade.
3. Leer `es_systems.cfg` y el launcher de RetroBat; mapear, **por sistema**, qué comando se ejecuta, qué emulador/core usa, y qué config de RetroArch se aplica (incluyendo overrides). Documentarlo en una tabla (sistema → emulador/core → cmdline real → ruta de ROMs → driver de video/fullscreen/network_cmd).
4. Probar a mano los comandos UDP de RetroArch (`GET_STATUS`, `PAUSE_TOGGLE`, `QUIT`, `SHOW_MSG`, `FAST_FORWARD`) con un juego de cada core relevante (N64 primero).
5. Reproducir el problema del N64 con `monedero.log` en mano y decidir entre: cambiar el modo de pantalla de ese core, usar OSD de RetroArch, o dibujar el overlay de otra forma.
6. Verificar el cierre limpio: que el código de salida sea 0 y que RetroBat vuelva al menú sin cartel.
7. Probar Dolphin / PCSX2 / DuckStation: pausa por suspensión vs hotkey, audio, cierre.
8. Mantener las prioridades de la sección 2: simpleza y estabilidad primero. Sugerir (no imponer) cualquier cambio de arquitectura.
9. Cada cambio: registrar en `monedero.log` lo necesario para diagnosticar sin volver a preguntar al usuario.

## 8b. Estado verificado con acceso al disco (Claude Code, 2026-10-05) — beta 3.6

Probado en esta PC con el disco del arcade conectado (aparece como `E:`; en el arcade es `D:`), lanzando juegos con
`emulatorLauncher.exe` igual que RetroBat. Lo que sigue **cierra** varias hipótesis de la sección 7:

| Hipótesis | Resultado |
|---|---|
| 1. N64 sin pausa/overlay | La sesión **sí** se detecta y la pausa **sí** funciona por UDP. Causa del overlay invisible: `parallel_n64` carga **OpenGL** (OPENGL32.dll) aunque `retroarch.cfg` diga `d3d11`, y en fullscreen exclusivo GL tapa ventanas topmost. Solución 3.6: `aviso=ambos` en `[n64]` (overlay + `SHOW_MSG` dentro de RetroArch) y topmost re-afirmado cada 250 ms. Falta ver en el CRT. |
| 2. RetroBat pisa `network_cmd_enable` | **No** lo toca: estaba en `false` en el `retroarch.cfg` global (por eso en el arcade nunca pausaba de verdad). Se puso en `true` (backup `retroarch.cfg.antes-monedero`) y sobrevive a los lanzamientos. |
| 3. Launcher de RetroBat | Confirmado `emulatorLauncher.exe`. Línea real: `retroarch.exe -L "...\cores\<core>_libretro.dll" "...\roms\<sistema>\<rom>"`. Con salida anormal registra `[Generator] Exit code 200`. |
| 4. QUIT lento / cierre inesperado | `quit_press_twice = "false"`. Cierres de prueba: `Cierre limpio por QUIT (1)` y `(2)`; **ningún** `Exit code 200` en el log de emulatorLauncher → RetroBat lo ve como salida normal. |
| 6. Comandos UDP | Verificados: `GET_STATUS` responde `GET_STATUS PLAYING nes,Mario Bros. (World),crc32=...` / `PAUSED ...`; `PAUSE_TOGGLE`, `QUIT`, `SHOW_MSG` funcionan. `FAST_FORWARD` sin probar. |
| 5. Standalone (suspend) | **Probado** con DuckStation, Dolphin y PCSX2 lanzados por emulatorLauncher: CPU 0 en pausa, ventana no se cuelga (IsHungAppWindow=false), reanuda bien. Cierre: `CloseMainWindow` fallaba → `Kill` → `Exit code 200`; ahora `WM_CLOSE` a todas las ventanas → código 0. Dolphin (D3D11 exclusivo) tapaba el cartel → `minimizar_al_pausar=1`. Audio durante la suspensión: no verificable acá. |
| Extra: PS2 en el arcade | PCSX2 salía en silencio: buscaba la BIOS `SCPH30004R.bin` en `bios\pcsx2\bios\` (estaba en `bios\`) → copiada. Además `Renderer=-1` (auto→Vulkan) fallaba → `Renderer = 3` (D3D11, lo que ya pedía `ps2.renderer=3`). Puede aparecer un diálogo de login de RetroAchievements (logros globales activos). |
| Extra: DuckStation | RetroBat lo fuerza a Vulkan, que falla en la PC de pruebas (en el arcade PS1 usa SwanStation en RetroArch, no DuckStation). |
| 7. Ctrl+Alt+F5 | Sin probar contra atajos de RetroArch/RetroBat. |

**Bug encontrado y corregido (3.5 → 3.5.1):** a los pocos segundos del lanzamiento RetroArch todavía no abrió el puerto
UDP (está cambiando de resolución); el `PAUSE_TOGGLE` se perdía (juego corriendo detrás del cartel) y al acreditar,
`ReanudarSiHaceFalta` "asumía pausado" y mandaba otro toggle a ciegas → podía **pausar al reanudar**. Ahora
`EsperarEstado()` reintenta hasta 8 s y nunca se manda un toggle sin estado confirmado.

**Novedades 3.6:** canvas CRT reescrito (`OverlayRenderer.cs`: alto entero de líneas 224/240/256 ×k, texto
rasterizado al ancho real del modo asumiendo tubo 4:3 — corrige texto aplastado en súper resoluciones de 300+ líneas),
fuentes TTF (Press Start 2P / Retro Gaming), sonido de moneda (MCI, sin dependencias), `aviso=overlay|osd|ambos`,
`sistemas_exentos` configurable, y **MonederoConfig.exe** (configurador aparte estilo BatGui, con vista previa del
cartel en cada modo del arcade). Ver `MonederoArcade\README.md`.

**Modos de video reales del arcade:** desktop 640x480; súper resoluciones 2560x240…2560x800; específicos
(256x224 sfc, 320x224 md, 304x224 ng, 336x240 pce, 1280x240 md, 3520x240 ss, 5376x240 pce, …); entrelazados
512x448, 640x448, 768x512. Todo en tubo 4:3.

**Pendiente:** ver en el CRT el cartel sobre N64/OpenGL y el sonido; standalone (Dolphin/PCSX2/DuckStation);
fast-forward; Ctrl+Alt+F5; juegos de PC nativos; seguridad de kiosko.

## 9. Convenciones útiles

- Idioma de respuestas, comentarios y textos de UI: español.
- Nada de rutas absolutas del usuario en el código; todo relativo al .exe (`RetroBat\monedero\`) o a `..\roms`.
- No generar archivos dentro de la carpeta de ROMs de RetroBat (ensucian los gamelists).
- Credenciales/PIN: solo en `config.ini`, nunca hardcodeados.
