# Captio

Herramienta de captura de pantalla para Windows centrada en rapidez y privacidad: captura una región, señálala con el lápiz, flechas, líneas, rectángulos o elipses, censúrala con bloques negros, y decide tú dónde termina la imagen.

- Sin cuentas, telemetría ni nube propia.
- Sin ninguna actividad de red: todo ocurre en tu equipo.
- Las censuras reemplazan los píxeles: la imagen final no tiene capas.

> «Captio» es un nombre provisional.

## Instalar

1. Descarga **Captio-Setup-x.y.z.exe** desde [Releases](https://github.com/emph4sis-dev/captio/releases/latest).
2. Ábrelo. No pide permisos de administrador: se instala solo para tu usuario, en `%LOCALAPPDATA%\Programs\Captio`, con acceso en el menú Inicio.
3. Si Windows SmartScreen avisa de una aplicación no reconocida, pulsa *Más información → Ejecutar de todas formas*. El instalador todavía no está firmado digitalmente.

Captio queda en la bandeja del sistema; pulsa **ImpPant** para capturar. Requiere Windows 10 (1809) u 11 de 64 bits y no necesita instalar .NET: lo incluye.

Para desinstalar: *Configuración de Windows → Aplicaciones → Captio → Desinstalar*. Se borran el programa y su entrada de *Iniciar con Windows*; tus capturas y tu configuración (`%APPDATA%\Captio`) se conservan.

## Estado

Versión 1.0. La especificación completa está en [docs/SPEC.md](docs/SPEC.md).

| Función | Estado |
|---|---|
| Captura de región con pantalla congelada (multimonitor, PerMonitorV2) | ✅ |
| Lápiz a mano alzada y formas: flecha, línea, rectángulo y elipse sin relleno; `Mayús` para línea recta a 45°, cuadrado o círculo | ✅ |
| Censura sólida, deshacer, rehacer, eliminar | ✅ |
| Selector compacto de 8 colores, recordado entre capturas | ✅ |
| Barra de edición centrada en la selección: arriba, abajo, a la izquierda o a la derecha | ✅ |
| Copiar (con copia guardada opcional), guardar en la carpeta de capturas | ✅ |
| Biblioteca sobre la carpeta de capturas (rápida con miles de imágenes): filtro, copiar, arrastrar, exportar, renombrar, borrar | ✅ |
| Iniciar con Windows | ✅ |
| Clips MP4 sin audio | ⏳ pendiente (oculto en la interfaz) |

## Uso

| Acción | Atajo |
|---|---|
| Capturar imagen | `ImpPant` (configurable) |
| Seleccionar / lápiz / forma actual / censurar | `V` / `P` / `F` / `R` |
| Menú de formas / panel de colores | clic en el botón, o `Mayús+F` / `C` |
| Línea a 45°, cuadrado o círculo perfecto mientras dibujas | mantener `Mayús` |
| Deshacer / rehacer / eliminar anotación | `Ctrl+Z` / `Ctrl+Y` / `Supr` |
| Copiar / guardar | `Ctrl+C` / `Ctrl+S` |
| Destino predeterminado | `Enter` |
| Cancelar sin conservar nada | `Ctrl+X` (también `Esc`) |

Si `ImpPant` abre la herramienta de recortes del sistema, desactiva en Windows *Configuración → Accesibilidad → Teclado → Usar la tecla Imprimir pantalla para abrir la captura de pantalla*, o elige otro atajo en la configuración de Captio.

## Guardado

- **`Ctrl+S`** guarda un PNG aplanado directamente en la **carpeta de capturas**, con el mismo nombre que usa Windows en su idioma: `Captura de pantalla 2026-10-30 013340.png` en español, `Screenshot 2026-10-30 013340.png` en inglés, `Capture d'écran …` en francés. Así se ordenan por fecha y conviven con las capturas de Windows.
- **`Ctrl+C`** copia la imagen al portapapeles. Si activas *Guardar automáticamente una copia al copiar*, Captio guarda también esa misma imagen en la carpeta de capturas, pero solo si la copia funcionó. Si el guardado falla, la imagen sigue en el portapapeles y verás un aviso.
- La **carpeta de capturas** predeterminada es la de Windows: *Imágenes\Screenshots*, resuelta con `FOLDERID_Screenshots`. Así respeta OneDrive, las carpetas redirigidas y el idioma. Captio no crea ninguna subcarpeta propia. Puedes elegir otra en la sección *Guardado* de Configuración; *Volver a la de Windows* restablece la predeterminada.
- La **biblioteca** es esa misma carpeta: lo que guardes, renombres o borres desde el Explorador también se ve en Captio.
- Al **cambiar de carpeta**, Captio pregunta si quieres mover las capturas existentes. Las copia, verifica cada una y solo después borra los originales. Si algo falla, todo queda como estaba, también tras un corte de luz.

### Si usabas la versión 0.1

La 0.1 guardaba las capturas en `%LOCALAPPDATA%\Captio\Library`. Si tienes capturas allí, Captio las sigue mostrando hasta que pulses *Mover a la carpeta de capturas de Windows* en Configuración. Al moverlas, los nombres que les pusiste pasan a ser nombres de archivo. Captio nunca las mueve sin preguntar.

## Juegos en pantalla completa

Si un juego (por ejemplo GTA V o FiveM) está en **pantalla completa exclusiva**, Windows tiene que sacarlo de ese modo para mostrar la pantalla de captura, y eso puede tardar segundos. Le pasa igual a cualquier herramienta de captura, incluida la de Windows. Para capturas instantáneas, pon el juego en **ventana sin bordes** (en GTA V: *Configuración → Gráficos → Tipo de pantalla*).

## Consumo

Captio está pensado para quedarse en la bandeja sin molestar: no usa CPU en reposo y devuelve la memoria a Windows después de cada captura (unos 32 MB en reposo, medidos en Windows 11).

## Inicio automático

En Configuración, sección *Sistema*: *Iniciar con Windows* (desactivado por defecto). Crea una entrada por usuario en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` y no necesita permisos de administrador. Captio arranca en la bandeja, sin abrir ventanas. Si desactivas la entrada desde el Administrador de tareas o la Configuración de Windows, Captio la muestra como desactivada.

Datos locales:

- Configuración: `%APPDATA%\Captio\settings.json`
- Diario de traslados en curso: `%LOCALAPPDATA%\Captio\migration.json` (solo existe durante un traslado)

## Compilar

Requisitos: Windows 10/11 y .NET SDK 10.

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/Captio
```


## Publicar una versión

El instalador se genera con [Inno Setup 6](https://jrsoftware.org/isinfo.php) a partir de [installer/Captio.iss](installer/Captio.iss):

```bash
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

Deja `artifacts/installer/Captio-Setup-<versión>.exe`: autocontenido (incluye .NET), win-x64 y precompilado con ReadyToRun para arrancar más rápido.

Para publicar, sube una etiqueta de versión. El flujo [Release](.github/workflows/release.yml) ejecuta las pruebas, construye el instalador y crea la versión en GitHub con el instalador adjunto:

```bash
git tag v1.0.1
```

```bash
git push origin v1.0.1
```

## Estructura

```
src/Captio.Core      Lógica sin interfaz: anotaciones y formas, censura, PNG, carpeta de capturas,
                     biblioteca y migración, inicio automático, posición de la barra, configuración
src/Captio           App WPF: bandeja, atajos globales, overlay de captura, ventanas
tests/               Pruebas de Captio.Core, del aplanado en WPF y del arranque en la bandeja
docs/SPEC.md         Especificación funcional
scripts/             Comprobación de términos, instalador e icono
installer/           Script de Inno Setup del instalador por usuario
```

## Contribuir

El repositorio no debe mencionar productos usados como referencia. Una lista de términos prohibidos, que vive fuera del repositorio (por defecto en `%USERPROFILE%\.captio-private\forbidden-terms.txt`) y nunca debe añadirse a él, se comprueba en cada `git push`: el hook `pre-push` revisa el árbol de trabajo, el contenido de los commits que se publican y sus mensajes, y bloquea el push si encuentra algo o si la lista no existe.

Actívalo una vez por clon:

```bash
git config core.hooksPath .githooks
```

También se puede ejecutar a mano:

```bash
powershell -ExecutionPolicy Bypass -File scripts/check-forbidden-terms.ps1
```

## Licencia

[MIT](LICENSE).
