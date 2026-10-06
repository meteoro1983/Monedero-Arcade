# Monedero Arcade — Beta 3.6

## Novedades de la 3.6

1. **MonederoConfig.exe** (nuevo, va junto a `monedero.exe`): configurador aparte estilo BatGui, entra en un escritorio
   de 640x480 (se maximiza solo). Pestañas: *General* (tecla de crédito con "Asignar tecla...", botón XInput con
   "Detectar...", pulsos, sonido + volumen + Probar, fuente, aspecto, textos, sistemas exentos, PIN), *Sistemas*
   (tiempos, exento, tipo de cartel y tecla por sistema), *Emuladores* (suspend/hotkey + tecla de pausa),
   *Juegos* (.ini por ROM, filtro, escaneo) y *Vista previa* (el cartel tal cual se ve en el CRT en cada modo del arcade).
   Todo se guarda en `config.ini` / `<sistema>\<rom>.ini`; el monedero lo relee sin reiniciarse.
2. **Canvas CRT**: el cartel se dibuja con alto entero de líneas (224/240/256, x2 en 448/480, x3 en 768) y el texto
   se rasteriza al ancho real del modo (2560, 3520, 5376, 256...) asumiendo tubo 4:3. Corrige el texto aplastado
   en súper resoluciones de 300+ líneas y el estirado en 512x448 / 640x448 / 768x512.
3. **Fuentes TTF** (`fuente=PressStart2P.ttf` o `Retro Gaming.ttf`) y **sonido de moneda** (`sonido_credito`,
   `volumen_sonido`) al acreditar.
4. **N64 / OpenGL**: `aviso=overlay|osd|ambos` por sistema o ROM. N64 viene en `ambos` (cartel + mensaje dentro de
   RetroArch, que se ve aunque OpenGL tape la ventana). El cartel además se re-afirma encima cada 250 ms.
5. **Pausa segura en RetroArch**: espera a que RetroArch responda por red y nunca manda PAUSE_TOGGLE a ciegas.
   Requiere `network_cmd_enable = "true"` en `retroarch.cfg`.
6. `sistemas_exentos` ahora es configurable en `[general]`.
7. **Standalone probados con RetroBat (DuckStation, Dolphin, PCSX2)**: pausa por suspensión real (CPU 0, la ventana
   no queda "No responde"), reanudación y cierre. **Cierre limpio**: `WM_CLOSE` a todas las ventanas del proceso
   (antes solo la principal → terminaba en `Kill` y RetroBat registraba `Exit code 200` = "cierre inesperado").
8. **`minimizar_al_pausar`** en `[emulador:*]` (por defecto solo Dolphin): Dolphin usa pantalla completa exclusiva
   D3D11 y tapa el cartel; RetroBat pisa su opción sin bordes en cada lanzamiento. Se minimiza antes de congelar y
   se restaura con foco al acreditar.
9. **PSP**: PPSSPP standalone (`PPSSPPWindows64.exe`, sección `[emulador:ppsspp]`) y core `ppsspp` de RetroArch,
   ambos probados con RetroBat (pausa, crédito, cierre limpio).

Compilar: igual que antes, en `MonederoArcade\` y en `MonederoConfig\` (comparte los .cs de config y dibujo).

---

# Beta 3.5 (notas anteriores)

Sistema de crédito virtual para RetroBat: pausa los juegos que no son arcade, muestra "INSERT COIN",
y cierra el emulador si nadie inserta crédito.

## Compilar (igual que antes)

Requiere el SDK de .NET 8. Desde la carpeta del proyecto:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o salida
```

Si hay errores de compilación, copiá el texto completo del error y mandámelo.

## Novedades de la 3.5 respecto de la 3 (que no se llegó a probar)

Todo lo de la beta 3 sigue: countdown de 20 s, cierre rápido con "GAME OVER", botón Select (XInput),
pausa verificada, log mejorado, `osd_retroarch`. Además:

1. **Emuladores standalone**: detecta Dolphin, PCSX2, DuckStation y Redream (además de RetroArch).
   El sistema sale de la carpeta que sigue a `roms\` en la ruta del juego (ej. `roms\gamecube\x.rvz` → `gamecube`).
2. **Pausa universal por suspensión de proceso** (`metodo_pausa=suspend`, valor por defecto en standalone):
   no necesita conocer hotkeys. Alternativa: `metodo_pausa=hotkey` + `tecla_pausa=...` en la sección
   `[emulador:dolphin]` (etc.) de `config.ini`. Los PID suspendidos se anotan en `suspendidos.txt` y se reanudan
   solos si el programa se cae o se reinicia.
3. **Interfaz de configuración**: `Ctrl+Alt+F5` o clic derecho en el ícono de la bandeja → Configuración.
   - Pestaña *Sistemas*: tabla editable de defaults por sistema (aviso, loop, countdown, fast-forward),
     botón *Agregar sistema*, y *Escanear ROMs del seleccionado* (crea los .ini individuales con `activo=0`;
     busca en `..\roms\<sistema>`, y si no existe te deja elegir la carpeta).
   - Pestaña *Juego actual*: detecta el juego en curso, edita sus tiempos, *Guardar* lo deja con `activo=1`
     (se aplica de inmediato a la sesión) y *Abrir .ini* abre su archivo.
   - Opcional: en `config.ini`, sección `[general]`, `pin_config=1234` pide un PIN antes de abrirla.
4. **Fast-forward automático** (solo RetroArch): `fast_forward=1` y `tiempo_fast_forward=N` (por sistema o por
   ROM). Acelera desde el segundo 1 hasta N s (siempre termina antes del primer aviso) y se apaga antes de pausar.
5. **`pulsos_por_credito`** funcional: cuenta N pulsos dentro de 2 s antes de dar el crédito (para monederos
   que mandan varios pulsos por moneda).
6. **Red de seguridad**: si el programa tiene una excepción o se cierra, reanuda cualquier proceso que haya
   suspendido.

## Qué probar (sugerido)

1. RetroArch como en la beta anterior (aviso, pausa, crédito con tecla 5 y con Select, cierre sin cartel de error).
2. N64 en RetroArch: mandar el `monedero.log`.
3. Un juego de Dolphin o PCSX2 o DuckStation con la pausa por suspensión: ¿pausa?, ¿se oye algún zumbido de audio
   mientras está suspendido?, ¿reanuda bien?, ¿cierra sin diálogos? Si el audio molesta, probar `metodo_pausa=hotkey`
   (verificar antes en el emulador cuál es la hotkey de pausa y ponerla en `tecla_pausa`).
4. Ctrl+Alt+F5 con un juego en marcha: editar el tiempo de aviso y guardar.
5. Fast-forward en un juego de RetroArch (`fast_forward=1`, `tiempo_fast_forward=15` en su sistema o su .ini activo).

## Notas importantes

- Tu `config.ini` actual no tiene las claves nuevas, pero funciona igual (usa valores por defecto). Las claves
  `[emulador:*]` y `[general]` se pueden agregar a mano o borrando `config.ini` para que se regenere (perdés lo editado).
- El countdown ya está en 20 s en los valores por defecto; si tu `config.ini` viejo tiene 15, editalo.
- La interfaz es una ventana normal de Windows: en una salida de 240p puede verse recortada. En ese caso
  usá la tabla desde el escritorio o editá los .ini a mano.
- Solo funciona con controles **XInput** el botón `credit_pad`; el teclado (`credit_key`) funciona siempre.
- Dolphin puede preguntar "¿detener emulación?" al cerrar la ventana si tiene activada esa confirmación.
- Sistemas arcade exentos (no se tocan): mame, neogeo, fbneo, fb, naomi, atomiswave, model2, model3, teknoparrot.
- Archivos que genera junto al .exe: `config.ini`, `monedero.log`, `suspendidos.txt` (solo si hay algo suspendido)
  y una carpeta por sistema con los .ini de cada ROM.
- Aún sin hacer: skins, tabla de resoluciones por aspect ratio, juegos de PC nativos, watchdog como proceso aparte.
