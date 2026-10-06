# Brief para Claude Code — Monedero Arcade

**Contexto en una línea:** programa de Windows (C# .NET 8 WinForms, carpeta `MonederoArcade/`, beta 3.5) que corre en segundo plano en una PC arcade de un bar con **RetroBat**, y fuerza una lógica de "monedero" en juegos no-arcade: tras un tiempo configurable **pausa el emulador, muestra "INSERT COIN" + countdown (20 s)**; con crédito (tecla `5` o Select XInput) reanuda y repite cada 20 min; sin crédito **cierra el emulador y vuelve limpio al menú de RetroBat**. Lee el detalle completo en `monedero_resumen_detallado.md` (leelo primero).

**Mi situación:** el código fue escrito por un Claude sin poder compilar ni probar. La beta 3.5 "todavía tiene problemas". Ahora tenés acceso al disco de la PC arcade (con RetroBat) para diseccionar los emuladores y hacer prueba y error.

**Prioridades:** 1) estabilidad y simpleza (cero errores, cero conflictos con RetroBat/emulador), 2) texto simple y retro, 3) todo configurable por .ini, 4) luego publicar como open source. Hablame en español, sé honesto sobre lo que no verificaste.

> **Actualización 2026-10-05 (beta 3.6):** N64, `network_cmd_enable`, cierre limpio y comandos UDP ya están
> verificados/resueltos — ver sección **8b** de `monedero_resumen_detallado.md`. Lo que sigue queda como historial.

**Problemas conocidos (a investigar):**
- **N64 en RetroArch**: no aparece el overlay y **no pausa** (sospecho: sesión no detectada por la línea de comandos, o pantalla completa exclusiva, o RetroBat sobrescribiendo `retroarch.cfg`/overrides por core con `network_cmd_enable=false`).
- **Cierre**: antes `QUIT` por UDP tardaba y RetroBat mostraba "el emulador se cerró inesperadamente" (código de salida ≠ 0). Se necesita salida limpia (código 0) y rápida, con el overlay "GAME OVER" tapando el juego.
- **Standalone** (Dolphin, PCSX2, DuckStation): pausa por suspensión de proceso sin probar (¿zumbido de audio?); hotkeys por defecto del `config.ini` son suposiciones.
- Comandos de RetroArch sin verificar: formato de `GET_STATUS`, `SHOW_MSG`, `FAST_FORWARD`.
- Resoluciones reales: 240p, 320x240, 384x224, **2560x240** (CRT EmuDriver cambia la resolución al lanzar), N64 entrelazado.

**Tareas sugeridas, en orden:**
1. Compilar y arreglar errores. Probar con `monedero.log` a mano.
2. **Backup** de `retroarch.cfg`, `es_systems.cfg`, `es_settings.cfg` y configs de emuladores. Proponerme cambios antes de tocar archivos del arcade.
3. Leer `es_systems.cfg` y el launcher de RetroBat (probablemente `emulatorLauncher.exe`, open source en GitHub): armar tabla sistema → emulador/core → línea de comandos real → config aplicada (driver de video, fullscreen, `network_cmd_enable`, `quit_press_twice`).
4. Probar manualmente los comandos UDP de RetroArch (puerto 55355) en un juego de cada core, N64 primero.
5. Resolver el N64 (pausa + overlay visibles) y el cierre limpio.
6. Probar y ajustar Dolphin / PCSX2 / DuckStation.
7. Registrar en el log todo lo necesario para diagnosticar sin preguntarme.

**Datos mínimos de config:** `config.ini` junto al .exe con secciones por sistema (nombres de RetroBat: `dreamcast, psx, n64, gamecube, wii, snes, nes, megadrive, pcengine…`) y `[default]`; un `.ini` por ROM en `monedero\<sistema>\<archivo>.ini` con `activo=0/1`; sistemas arcade exentos: `mame, neogeo, fbneo, fb, naomi, atomiswave, model2, model3, teknoparrot`. Atajo de configuración: `Ctrl+Alt+F5`.
