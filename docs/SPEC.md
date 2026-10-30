# Captio — Especificación funcional

Documento de referencia para diseño e implementación. Describe necesidades, estados, casos límite y criterios de aceptación. Es la única fuente de requisitos del proyecto.

## 1. Propósito

Captio es una herramienta de captura de pantalla para Windows centrada en rapidez y privacidad: capturar, señalar o censurar, y decidir de forma explícita dónde termina la imagen.

Principios:

- Windows 11 como primera plataforma. Código abierto.
- Sin cuentas, telemetría ni nube propia.
- Ninguna actividad de red salvo una subida solicitada por el usuario.
- Imgur es un destino externo opcional, solo para imágenes.
- Biblioteca local permanente solo para lo que el usuario guarda.

## 2. Estados de la aplicación

| Estado | Descripción | Salidas |
|---|---|---|
| `Idle` | En la bandeja del sistema, sin ventanas visibles. | Atajo de imagen → `Selecting`; atajo de clip → `ClipSelecting` |
| `Selecting` | Pantalla congelada y atenuada; el usuario arrastra una región. | Región válida → `Editing`; `Esc` → `Idle` |
| `Editing` | Región ajustable, barra de herramientas visible, anotaciones editables. | Copiar / Guardar → `Idle`; Subir → `Uploading`; `Esc` → `Idle` |
| `Uploading` | Imagen aplanada en memoria, subida en curso, cancelable. | Éxito → `UploadDone`; error → `UploadFailed`; cancelar → `UploadFailed` |
| `UploadDone` | Se muestra el enlace con `Copiar enlace`, `Abrir`, `Cerrar`. | Cerrar → `Idle` (se descartan imagen, enlace y token) |
| `UploadFailed` | Imagen en memoria con `Reintentar`, `Copiar`, `Guardar`, `Descartar`. | Reintentar → `Uploading`; Guardar → `Idle` (con entrada en biblioteca); Descartar o cerrar → `Idle` |
| `ClipSelecting` | Selección de región para un clip. | Región → `Recording`; `Esc` → `Idle` |
| `Recording` | Grabación en curso con duración y botón detener. | Detener → `Idle` (MP4 en biblioteca) |

`Esc` en `Selecting` o `Editing` nunca deja archivos ni entradas en la biblioteca.

## 3. Captura de imagen

1. El atajo global (por defecto `PrtSc`, configurable) congela todas las pantallas y las atenúa.
2. El usuario arrastra una región. La región se puede mover y redimensionar antes de confirmar.
3. Aparece una barra de herramientas junto a la región.
4. Herramientas:
   - **Flecha**: color y grosor seleccionables.
   - **Censura**: rectángulo negro sólido.
   - **Seleccionar**: mover o eliminar anotaciones existentes, mover o redimensionar la región.
   - Deshacer (`Ctrl+Z`), rehacer (`Ctrl+Y`), eliminar anotación seleccionada (`Supr`).
5. Destinos: Copiar (`Ctrl+C`), Guardar (`Ctrl+S`), Subir a Imgur (`Ctrl+U`).
6. `Enter` ejecuta el destino predeterminado configurado.

Fuera de alcance inicial: lápiz, texto, formas adicionales, OCR, plantillas, captura desplazable.

Métrica: la superficie de selección aparece en menos de 200 ms tras el atajo en el equipo de prueba.

## 4. Censura segura

- Mientras se edita, una censura se puede mover o eliminar.
- Al confirmar cualquier destino, la imagen se aplana: los píxeles bajo cada censura se reemplazan por negro opaco (`#000000`, alfa 255).
- La salida es un único mapa de bits, sin capas ni datos recuperables.
- El original sin censurar nunca se escribe en disco ni se transmite.
- Las salidas PNG no llevan metadatos de texto, EXIF ni perfiles innecesarios.

## 5. Destinos

### 5.1 Copiar

Coloca la imagen aplanada en el portapapeles. No crea archivos ni entradas en la biblioteca.

### 5.2 Guardar

Escribe un PNG aplanado en la carpeta de la biblioteca y registra la entrada.

### 5.3 Subir a Imgur

1. Aplanar y aplicar censuras.
2. Codificar en memoria; ningún archivo temporal.
3. `POST` por HTTPS a la API oficial, cabecera `Authorization: Client-ID <id>`.
4. Mostrar el enlace resultante con `Copiar enlace`, `Abrir`, `Cerrar`.
5. El enlace no se copia automáticamente.
6. Al cerrar se descartan imagen, enlace y token de eliminación.
7. No se crea entrada en la biblioteca, miniatura ni registro.

Consentimiento: antes del primer uso se muestra este texto y el usuario debe aceptarlo de forma expresa:

> Esta imagen se enviará a Imgur. No aparecerá públicamente en su comunidad, pero cualquiera con el enlace podrá verla. Captio no conservará el archivo, el enlace ni la clave necesaria para eliminarlo posteriormente.

El aviso puede consultarse de nuevo desde Configuración. Solo tras aceptarlo se puede elegir Imgur como destino predeterminado.

Restricciones técnicas:

- Solo `Client-ID`; nunca `Client Secret`, OAuth ni cuentas.
- No se almacenan URL, identificador ni `deletehash`.
- Enlaces y tokens no aparecen en logs ni informes de error.
- Solo imágenes; los clips no se suben nunca.
- No se promete permanencia del enlace.
- La interfaz advierte que Imgur puede comprimir o convertir imágenes grandes.
- Los límites de uso se leen de la respuesta del servicio; no hay cifras fijas en el código.

Fallos:

- La imagen permanece en memoria; se ofrecen `Reintentar`, `Copiar`, `Guardar`, `Descartar`.
- Nunca se guarda automáticamente como respaldo.
- Cerrar la ventana o la aplicación descarta la imagen.
- Un límite de uso muestra un mensaje claro, sin reintentos automáticos.
- La subida en curso se puede cancelar.

## 6. Biblioteca local

Contiene únicamente capturas guardadas y clips. Persiste entre reinicios.

Cada entrada: miniatura, fecha, dimensiones, tipo (imagen o video) y nombre editable.

Acciones: filtrar por tipo y fecha, copiar, arrastrar a otra aplicación, exportar, renombrar, borrar definitivamente (con confirmación).

Ubicación: `%LOCALAPPDATA%\Captio\Library\` con un índice `index.json`. Si un archivo del índice falta en disco, la entrada se omite y el índice se repara. Si el índice está dañado, se reconstruye a partir de los archivos presentes.

## 7. Clips de video

1. El atajo de clip (por defecto `Shift+PrtSc`) inicia la selección de región.
2. Se inicia la grabación; se muestran duración y botón detener.
3. Al detener se guarda un MP4 sin audio en la biblioteca.

Fuera de alcance: audio, cámara, GIF, pausa, anotación durante la grabación, editor, subida.

## 8. Configuración

Archivo `%APPDATA%\Captio\settings.json`:

- Atajos de imagen y clip.
- Destino predeterminado de `Enter`: Copiar, Guardar o Imgur (este último solo tras consentimiento).
- Estado del consentimiento de Imgur.
- Color y grosor de flecha recordados.

Ninguna de estas opciones contiene datos de subidas.

## 9. Casos límite

- Región de tamaño cero o menor a 2 px: se ignora y se vuelve a `Selecting`.
- Varios monitores con distinto escalado: cada monitor se congela por separado y la región se limita a un monitor.
- El atajo ya está en uso por otra aplicación o por el sistema: se avisa al iniciar y se sugiere cambiarlo.
- Sin `Client-ID` configurado en la compilación: el botón de Imgur aparece deshabilitado con explicación.
- Sin conexión, tiempo agotado, respuesta 429 o 5xx: estado `UploadFailed` con mensaje específico.
- Segunda instancia de la aplicación: se cierra y la primera sigue activa.
- Disco lleno al guardar: mensaje de error, la imagen sigue en el editor.

## 10. Criterios de aceptación

- [ ] La selección aparece en menos de 200 ms en el equipo de prueba.
- [ ] Copiar o subir requiere como máximo una acción tras editar.
- [ ] No hay tráfico de red salvo durante una subida solicitada.
- [ ] Nunca se sube una captura sin acción explícita o preferencia consentida.
- [ ] La censura reemplaza los píxeles antes de guardar o transmitir.
- [ ] Imgur recibe solo la versión aplanada.
- [ ] El modo Imgur no crea archivos, miniaturas ni registros locales.
- [ ] URL y `deletehash` desaparecen al cerrar el resultado.
- [ ] Un fallo de red no provoca un guardado automático.
- [ ] Cancelar no deja archivos ni entradas en la biblioteca.
- [ ] La biblioteca persiste tras reiniciar.
- [ ] Los clips nunca se envían a Imgur.
- [ ] El repositorio pasa la comprobación de términos prohibidos.
