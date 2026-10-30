# Captio

Herramienta de captura de pantalla para Windows centrada en rapidez y privacidad: captura una región, señálala con flechas o censúrala con bloques negros, y decide tú dónde termina la imagen.

- Sin cuentas, telemetría ni nube propia.
- Sin actividad de red, salvo cuando pides subir una imagen a Imgur.
- Las censuras reemplazan los píxeles: la imagen final no tiene capas.

> «Captio» es un nombre provisional.

## Estado

Versión 0.1, en desarrollo. La especificación completa está en [docs/SPEC.md](docs/SPEC.md).

| Función | Estado |
|---|---|
| Captura de región con pantalla congelada (multimonitor, PerMonitorV2) | ✅ |
| Flecha (color y grosor), censura sólida, deshacer/rehacer/eliminar | ✅ |
| Copiar, guardar en biblioteca | ✅ |
| Subida anónima a Imgur con consentimiento, sin persistencia | ✅ |
| Biblioteca: filtros, copiar, arrastrar, exportar, renombrar, borrar | ✅ |
| Configuración: atajos, destino de Enter, consentimiento | ✅ |
| Clips MP4 sin audio | ⏳ pendiente |

## Uso

| Acción | Atajo |
|---|---|
| Capturar imagen | `ImpPant` (configurable) |
| Grabar clip | `Mayús+ImpPant` (configurable) |
| Herramientas: seleccionar / flecha / censurar | `V` / `A` / `R` |
| Deshacer / rehacer / eliminar anotación | `Ctrl+Z` / `Ctrl+Y` / `Supr` |
| Copiar / guardar / subir a Imgur | `Ctrl+C` / `Ctrl+S` / `Ctrl+U` |
| Destino predeterminado | `Enter` |
| Cancelar sin conservar nada | `Esc` |

Si `ImpPant` abre la herramienta de recortes del sistema, desactiva en Windows *Configuración → Accesibilidad → Teclado → Usar la tecla Imprimir pantalla para abrir la captura de pantalla*, o elige otro atajo en la configuración de Captio.

Datos locales:

- Configuración: `%APPDATA%\Captio\settings.json`
- Biblioteca: `%LOCALAPPDATA%\Captio\Library\`

## Imgur

Las subidas son anónimas y ocultas, **no privadas**: cualquiera con el enlace puede ver la imagen. Captio no guarda la imagen, el enlace ni el token de eliminación, y te pide aceptar este aviso antes de la primera subida.

Solo se usa el `Client-ID` público de una aplicación registrada en Imgur; nunca un `Client Secret` ni OAuth. Se inyecta al compilar:

```bash
dotnet build -p:ImgurClientId=TU_CLIENT_ID
```

Para desarrollo también se puede usar la variable de entorno `CAPTIO_IMGUR_CLIENT_ID`. Sin Client-ID, el botón de Imgur aparece desactivado.

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

## Estructura

```
src/Captio.Core      Lógica sin interfaz: anotaciones, censura, PNG, Imgur, biblioteca, configuración
src/Captio           App WPF: bandeja, atajos globales, overlay de captura, ventanas
tests/               Pruebas de Captio.Core y del aplanado de imágenes en WPF
docs/SPEC.md         Especificación funcional
scripts/             Comprobaciones del repositorio
```

## Contribuir

Antes de cada entrega, ejecuta la comprobación de términos prohibidos. La lista de términos vive fuera del repositorio (por defecto en `%USERPROFILE%\.captio-private\forbidden-terms.txt`) y nunca debe añadirse a él:

```bash
powershell -ExecutionPolicy Bypass -File scripts/check-forbidden-terms.ps1
```
