# Captio — Sistema visual

Guía breve para el README, las imágenes y cualquier material público. La interfaz de la aplicación sigue el tema Fluent de Windows; esta guía toma de ella su acento y su barra de edición.

## Idea

Precisión, rapidez y privacidad. Una herramienta nativa de Windows que aparece, resuelve la tarea y desaparece. La presentación es silenciosa: neutros fríos, un solo acento, jerarquía tipográfica clara y espacio negativo. Sin degradados decorativos, sin efecto cristal y sin tarjetas flotantes repetidas.

## Color

Un único acento, el índigo del icono. Todo lo demás es neutro.

| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| `--bg` | `#F6F7F9` | `#0F1115` | Fondo de página |
| `--surface` | `#FFFFFF` | `#161920` | Paneles |
| `--surface-2` | `#EEF0F4` | `#1D2129` | Bandas y bloques de código |
| `--ink` | `#14161B` | `#ECEEF2` | Texto principal |
| `--ink-2` | `#4A505C` | `#A3A9B5` | Texto secundario (contraste ≥ 7:1) |
| `--line` | `#DCE0E7` | `#2A2F38` | Separadores de 1 px |
| `--accent` | `#4F5BD5` | `#9AA3F5` | Enlaces, foco y acción principal (≥ 5:1 sobre el fondo) |
| `--accent-ink` | `#FFFFFF` | `#0F1115` | Texto sobre el acento |

Colores tomados del editor (iguales en los dos temas): barra `#1B1C21`, borde de barra `#3A3C45`, contorno de selección `#7C9CFF`, anotación roja `#FF0000` y censura `#000000`.

El tema se elige con `prefers-color-scheme`.

## Tipografía

- **Familia:** `"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`; los títulos usan `"Segoe UI Variable Display"` primero. Solo fuentes del sistema, sin descargas.
- **Escala (px):** 14 · 16 · 18 · 22 · 28 · 40, y el título principal de 40 a 56.
- **Pesos:** 400 y 600.
- **Interlineado:** 1.5 para texto y 1.1 para títulos. Ancho de lectura de 65 caracteres como máximo.

## Espaciado, radios y profundidad

- **Escala de 4 px:** 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96. Las secciones se separan con 96 px en escritorio y 64 px en móvil.
- **Radios:** 6 px para controles, 10 px para paneles y 14 px para marcos de imagen.
- **Sombras:** solo en lo que flota, como la barra de edición: `0 8px 24px rgb(0 0 0 / .18)`. Los paneles en reposo se separan con una línea o un cambio de tono.

## Movimiento

Para cualquier material animado futuro (una web, un vídeo de demostración), el movimiento explica el producto; no decora.

- Curva de entrada `cubic-bezier(0.16, 1, 0.3, 1)`. Duraciones de 120 ms para respuestas, de 200 ms para hover y foco, y de 400 ms para cambios de disposición. Las salidas duran un 30 % menos que las entradas.
- Solo se animan `transform`, `opacity` y `clip-path`. Sin rebotes, paralaje ni animaciones infinitas.
- Siempre con una variante para `prefers-reduced-motion` que muestre el estado final.

## Imágenes

Las capturas se generan con el editor real sobre una pantalla de demostración sintética (`tests/Captio.Tests/DocumentationImagesTests.cs`): sin escritorio real, archivos ni datos personales. Para regenerarlas:

```bash
$env:CAPTIO_DOCS_IMAGES_DIR = "docs/images"; dotnet test tests/Captio.Tests --filter DocumentationImages
```

Los iconos se dibujan en SVG con un trazo uniforme. Nunca se usan emojis como iconos.
