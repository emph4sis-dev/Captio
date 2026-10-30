# Captio — Especificación funcional

Documento de referencia para diseño e implementación. Describe necesidades, estados, casos límite y criterios de aceptación. Es la única fuente de requisitos del proyecto.

## 1. Propósito

Captio es una herramienta de captura de pantalla para Windows centrada en rapidez y privacidad: capturar, señalar o censurar, y decidir de forma explícita dónde termina la imagen.

Principios:

- Windows 11 como primera plataforma. Código abierto.
- Sin cuentas, telemetría ni nube propia.
- Ninguna actividad de red: todo ocurre en el equipo del usuario.
- Las capturas guardadas viven en una carpeta normal del usuario, por defecto la carpeta de capturas de Windows.

## 2. Estados de la aplicación

| Estado | Descripción | Salidas |
|---|---|---|
| `Idle` | En la bandeja del sistema, sin ventanas visibles. | Atajo de imagen → `Selecting` |
| `Selecting` | Pantalla congelada y atenuada; el usuario arrastra una región. | Región válida → `Editing`; `Esc` → `Idle` |
| `Editing` | Región ajustable, barra de herramientas visible, anotaciones editables. | Copiar / Guardar → `Idle`; `Ctrl+X` o `Esc` → `Idle` |

Cancelar (`Ctrl+X` o `Esc`) en `Selecting` o `Editing` nunca deja archivos ni entradas en la biblioteca.

La grabación de clips (§9) todavía no existe y no aparece en ningún menú, atajo ni pantalla de configuración.

## 3. Captura de imagen

1. El atajo global (por defecto `ImpPant`, configurable) congela todas las pantallas y las atenúa.
2. El usuario arrastra una región. La región se puede mover y redimensionar antes de confirmar.
3. Aparece la barra de edición junto a la región (§4).
4. Herramientas:
   - **Seleccionar** (`V`): mover o eliminar anotaciones; mover o redimensionar la región. Es la herramienta activa al aparecer la barra, para poder ajustar la región antes de elegir otra.
   - **Forma**: el botón muestra la forma actual; al pulsarlo se abre un menú con flecha, rectángulo sin relleno y elipse sin relleno, igual que el panel de color. `F` activa la forma actual y `Mayús+F` abre el menú.
   - **Censurar** (`R`): rectángulo negro sólido.
   - **Color** (`C`): panel con ocho colores.
   - Deshacer (`Ctrl+Z`), rehacer (`Ctrl+Y`), eliminar anotación seleccionada (`Supr`).
5. Destinos: Copiar (`Ctrl+C`) y Guardar (`Ctrl+S`). `Enter` ejecuta el destino predeterminado. Cancelar: `Ctrl+X` o `Esc`.

### 3.1 Formas

- Las tres formas usan el color seleccionado y un grosor fijo de 6 DIP. El grosor se convierte a píxeles según la escala del monitor, así que se ve igual en todas las pantallas.
- Rectángulo y elipse solo tienen contorno. Mientras se dibuja, `Mayús` convierte el rectángulo en cuadrado y la elipse en círculo, conservando la dirección del arrastre.
- Para seleccionar una forma hay que hacer clic sobre su trazo (con una tolerancia de 6 DIP). El interior vacío de un rectángulo o de una elipse no la selecciona. Una censura sí se selecciona por toda su área, porque es sólida.
- Las formas se pueden mover, eliminar, deshacer y rehacer igual que las demás anotaciones.
- La salida las recorta a la región capturada.
- La forma elegida en el menú se recuerda entre capturas y reinicios. El icono del botón muestra la forma actual.

### 3.2 Color

- El botón muestra un icono de paleta del mismo tamaño que los demás, con una pastilla del color actual en la esquina, sin contorno blanco. Al pulsarlo se abre un panel pequeño con rojo, naranja, amarillo, verde, cian, azul, violeta y magenta, todos con saturación del 100 % y luminosidad HSL del 50 %. El color actual se marca con el fondo de selección.
- El panel se cierra al elegir un color, al pulsar `Esc` o al hacer clic fuera. Se recorre con las flechas o con `Tab`, se elige con `Enter` o `Espacio`, y cada color tiene un nombre accesible.
- El color se aplica a flechas, rectángulos y elipses. La censura siempre es negra.
- El color elegido se guarda al momento. Un color antiguo que no está en la paleta se sustituye por el tono más cercano de la paleta.

### 3.3 Grosor

El selector de grosor no aparece. El valor guardado (`ArrowThickness`) se conserva por compatibilidad y para una futura sección «Avanzado», pero mientras `StrokeThicknessCustomizable` sea `false` todas las formas usan el grosor predeterminado.

Fuera de alcance: lápiz, texto, más formas, OCR, plantillas y captura desplazable.

Métrica: la superficie de selección aparece en menos de 200 ms tras el atajo, en el equipo de prueba.

## 4. Barra de edición

Orden: Seleccionar · Forma (abre su menú) · Censurar · Color (abre su panel) │ Deshacer · Rehacer · Eliminar │ Copiar · Guardar · Cancelar.

- **Orientación**: preferencia persistente con cuatro valores: Arriba, Abajo, Izquierda y Derecha (la predeterminada). Con Izquierda o Derecha la barra es vertical; con Arriba o Abajo, horizontal. Siempre muestra iconos con tooltip.
- **Posición**:
  1. Fuera de la selección, en el lado preferido y centrada respecto a ella: centrada en vertical si está a la izquierda o a la derecha, y en horizontal si está arriba o abajo. Si centrada se saldría de la pantalla, se desplaza lo justo para caber.
  2. Si no cabe, en el lado opuesto.
  3. Si tampoco cabe, dentro de la pantalla sin tapar el centro de la selección.
  4. Nunca queda fuera de la pantalla.
- La posición se calcula en DIP a partir de la región en píxeles físicos y de la escala del monitor, así que se comporta igual en monitores con distinta escala.
- Cambiar la orientación no modifica la región ni las anotaciones.
- Tamaño compacto: botones de 28 DIP (icono de 16 DIP), 2 DIP entre botones solo en la dirección de la barra y 4 DIP de margen interior. La barra vertical mide unos 38 DIP de ancho.
- Los botones que no están disponibles en un momento dado (deshacer, rehacer, eliminar) siguen en su sitio y aparecen desactivados: la barra no cambia de tamaño.

## 5. Censura segura

- Mientras se edita, una censura se puede mover o eliminar.
- Al confirmar cualquier destino, la imagen se aplana. Primero se dibujan las formas y después los píxeles bajo cada censura se reemplazan por negro opaco (`#000000`, alfa 255). Ninguna forma dibujada encima puede revelar nada.
- La salida es un único mapa de bits, sin capas ni datos recuperables.
- El original sin censurar nunca se escribe en disco ni se transmite.
- Las salidas PNG solo conservan los fragmentos necesarios para decodificar la imagen: sin texto, EXIF, fechas, perfiles ni dimensiones físicas.

## 6. Destinos

### 6.1 Copiar

1. Se aplanan las formas y las censuras.
2. La imagen se copia al portapapeles.
3. Si la copia funcionó y está activada la opción «Guardar automáticamente una copia al copiar», se guardan esos mismos bytes PNG (sin volver a renderizar) en la carpeta de capturas, y aparecen en la biblioteca.

Casos:

- Si la copia falla, el editor sigue abierto y no se guarda nada.
- Si la copia funciona pero el guardado falla, el portapapeles se conserva, el editor se cierra y una notificación avisa de que la imagen se copió pero no se pudo guardar.
- La opción está desactivada por defecto.

### 6.2 Guardar

`Ctrl+S` escribe el PNG aplanado directamente en la carpeta de capturas (§7). Si la carpeta no se puede usar, se explica el motivo, se ofrece elegir otra y el editor sigue abierto. Nunca se guarda en otra ubicación sin preguntar.

Nombre del archivo: la palabra de Windows para «captura de pantalla» en el **idioma de pantalla** del sistema, seguida de la fecha y hora locales como `aaaa-MM-dd HHmmss` (`Captura de pantalla 2026-10-30 013340.png`, `Screenshot 2026-10-30 013340.png`).

- El idioma se busca de lo más concreto a lo general (`pt-PT` → `pt`; `zh-TW` → `zh-Hant`). Hay unos 25 idiomas incluidos y los demás usan `Screenshot`.
- La parte de fecha no depende del formato regional: siempre usa el calendario gregoriano, cifras occidentales y 24 horas, para que los archivos ordenados por nombre queden en orden cronológico.
- Dos capturas en el mismo segundo reciben « (2)», « (3)»… y nunca se sobrescribe un archivo.

## 7. Carpeta de capturas y biblioteca

### 7.1 Ubicación

- **Predeterminada**: la carpeta conocida de Windows `FOLDERID_Screenshots`, normalmente *Imágenes\Screenshots*. Se resuelve con `SHGetKnownFolderPath`, así que respeta la redirección de Imágenes, OneDrive, el idioma y la ubicación real del perfil. Nunca se construye una ruta a mano.
- Los archivos se guardan directamente en esa carpeta. No se crea ninguna subcarpeta `Captio`.
- Si la carpeta todavía no existe, se le pide a Windows que la cree (`KF_FLAG_CREATE`) en el momento de guardar. Solo se crea esa carpeta.
- Si Windows no puede resolverla o no es escribible, se informa del problema y se ofrece elegir otra. No hay ninguna ubicación de reserva silenciosa.
- **Personalizada**: se elige con el selector de carpetas nativo. Antes de aceptarla se comprueba que la ruta es completa, que la carpeta existe y que se puede escribir en ella. Una carpeta personalizada nunca se crea.
- *Volver a la de Windows* restablece `FOLDERID_Screenshots`. Elegir esa misma carpeta con el selector equivale a restablecer.

### 7.2 Biblioteca

La biblioteca es la carpeta de capturas. Cada PNG de la carpeta es un elemento y su nombre de archivo es su nombre. No hay índice ni copia interna, así que lo que se añade, renombra o borra desde el Explorador aparece en la biblioteca al volver a ella.

Cada entrada muestra miniatura, fecha, dimensiones y nombre.

Rendimiento: el listado solo lee las entradas del directorio, sin abrir ningún archivo, y se hace en segundo plano. Las miniaturas y dimensiones se cargan en segundo plano, como mucho tres a la vez, y la biblioteca muestra 60 elementos con un botón «Mostrar más». Guardar nunca vuelve a listar la carpeta, aunque tenga miles de imágenes.

Acciones:

- Filtrar por fecha.
- Copiar.
- Arrastrar a otra aplicación.
- Exportar.
- Renombrar el archivo, conservando la extensión y sin sobrescribir otro archivo.
- Borrar definitivamente, con confirmación.
- Abrir la carpeta y cambiarla.

### 7.3 Cambio de carpeta y migración

- Al cambiar de carpeta, si la anterior contiene capturas, se pregunta: **Sí** (moverlas), **No** (dejarlas donde están; la biblioteca mostrará solo la carpeta nueva) o **Cancelar**.
- Al mover, cada archivo:
  1. Se copia con un nombre temporal.
  2. Se verifica con SHA-256.
  3. Se renombra a su nombre final, sin sobrescribir nunca un archivo existente.
- Cuando todos los archivos están verificados, la configuración cambia a la carpeta nueva y solo entonces se borran los originales.
- Un diario (`%LOCALAPPDATA%\Captio\migration.json`) permite recuperarse de un corte:
  - si la copia no había terminado, se deshace y los originales quedan intactos;
  - si ya estaba verificada, se termina el traslado.
- Cualquier fallo deja el origen intacto y la carpeta sin cambiar.
- **Instalaciones de la versión 0.1**: guardaban las capturas en `%LOCALAPPDATA%\Captio\Library` con un `index.json`.
  - Si esa carpeta tiene capturas, sigue siendo la carpeta de capturas hasta que el usuario decida moverlas. Configuración muestra un aviso con el botón «Mover a la carpeta de capturas de Windows».
  - Al moverlas, los nombres que el usuario había puesto en la biblioteca pasan a ser nombres de archivo, se conservan las fechas y se eliminan el índice y la carpeta vacía.
  - Si no hay capturas antiguas, se usa directamente la carpeta de Windows.

## 8. Inicio automático

- Opción «Iniciar Captio al iniciar sesión en Windows», desactivada por defecto.
- Es por usuario y no necesita privilegios de administrador. En la distribución actual sin empaquetar usa el valor `Captio` de `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, con la ruta del ejecutable entre comillas y el argumento `--autostart`.
- El estado mostrado se consulta siempre al sistema. Se ve desactivado si:
  - la entrada no existe;
  - apunta a otro ejecutable;
  - Windows la marcó como deshabilitada (Administrador de tareas o Configuración → Aplicaciones → Inicio).
- Activar la opción desde Captio limpia esa marca de deshabilitado.
- Al iniciarse, con o sin `--autostart`, Captio queda en la bandeja sin abrir ventanas.
- La responsabilidad está detrás de la interfaz `ILaunchAtLogin`, para poder sustituirla por `StartupTask` si la aplicación se empaqueta.

## 9. Clips de video (pendiente)

La grabación de clips MP4 sin audio está planificada, pero no implementada. Hasta que funcione no hay atajo registrado ni opciones en la bandeja ni en Configuración. `ClipHotkey` se conserva en la configuración sin usarse.

## 10. Configuración

Una sola página. La ventana se ajusta a su contenido y solo muestra una barra de desplazamiento si la pantalla es más baja. Cada cambio se aplica al momento: el pie confirma «Guardado» y hay un único botón «Cerrar».

Distribución, en el orden en que se usa Captio (capturar → editar → guardar → sistema). Cada grupo es un solo panel dividido por filas: título y descripción a la izquierda, control a la derecha, como en Configuración de Windows 11.

- **Encabezado**: la marca de Captio, «Configuración» y la versión.
- **Captura**
  - *Atajo de captura*: se muestra como una tecla. Al pulsarla espera la nueva combinación (`Esc` cancela). Si otro programa ya la usa, se mantiene la anterior y la fila explica por qué.
  - *Al pulsar Enter*: selector segmentado Copiar / Guardar.
  - *Barra de edición*: cuatro mosaicos que dibujan la selección y la barra en cada lado.
- **Guardado**
  - *Carpeta de capturas*: cuántas imágenes hay y la ruta (recortada, con la ruta completa en el tooltip); `Elegir…` y un botón para abrir la carpeta. Solo cuando la carpeta no es la de Windows aparece una fila con *Volver a la de Windows*, que para una biblioteca de la versión 0.1 dice *Moverlas a la de Windows*.
  - *Guardar también al copiar*: interruptor.
- **Sistema**
  - *Iniciar con Windows*: interruptor que refleja el estado real del sistema.

Los controles usan los colores del tema de Windows (claro u oscuro). Las explicaciones de uso solo aparecen cuando hacen falta: al grabar un atajo o ante un problema.

Archivo `%APPDATA%\Captio\settings.json`, escrito de forma atómica:

| Clave | Predeterminado |
|---|---|
| `ImageHotkey` | `ImpPant` |
| `ClipHotkey` | `Mayús+ImpPant` (reservado, sin usar) |
| `DefaultDestination` | `Copy` |
| `LaunchAtLogin` | `false` (última petición; el sistema manda) |
| `CopyAlsoSaves` | `false` |
| `CaptureFolder` | `null` = `FOLDERID_Screenshots` |
| `ToolbarSide` | `Right` |
| `LastShape` | `Arrow` |
| `ArrowArgb` | rojo `#FF0000` |
| `ArrowThickness` | `6` (oculto) |

- Las claves que faltan toman su valor predeterminado.
- Los valores fuera de rango se normalizan.
- Un valor que esta versión no reconoce (por ejemplo un destino eliminado) vuelve a su predeterminado sin perder el resto. Un archivo dañado hace que se usen todos los predeterminados, sin impedir el inicio.
- Las rutas relativas no se guardan nunca.
- La configuración solo guarda preferencias: ningún dato de capturas concretas.

## 11. Rendimiento y consumo

Captio pasa casi todo el tiempo en la bandeja y debe ser ligero:

- **En reposo:** sin temporizadores ni sondeos, con 0 ms de CPU medidos en 10 s. Usa una sola tarea de GC, sin hilo de recolección en segundo plano.
- **Al volver a la bandeja:** tras cada captura y al cerrar Biblioteca o Configuración, se compacta la memoria y se recorta el conjunto de trabajo cuando la interfaz queda libre. Medido: unos 32 MB tras iniciar o tras capturar, frente a unos 125 MB con la captura abierta.
- **Copia de pantalla:** `BitBlt` sin `CAPTUREBLT`, porque el compositor ya incluye las ventanas superpuestas y esa opción ralentiza la copia (sobre todo con juegos DirectX) y hace parpadear el cursor. Medido en 3000×1920: 43,5 ms con `CAPTUREBLT` y 38,3 ms sin ella.
- **Juegos en pantalla completa exclusiva:** mostrar la superficie de selección obliga a Windows a sacar el juego de ese modo, lo que puede tardar segundos. Es una limitación del sistema; con «ventana sin bordes» la captura es inmediata.
- **Pendiente:** unos 155 MB de memoria privada reservada (no activa) persisten tras la primera captura. No es memoria del recolector de .NET; es probable que sea una reserva nativa de WPF o del controlador gráfico. Una optimización futura podría capturar con DXGI Desktop Duplication y revisar esa reserva.

## 12. Casos límite

- Región de tamaño cero o menor a 2 px: se ignora y se vuelve a `Selecting`.
- Varios monitores con distinto escalado: cada monitor se congela por separado y la región se limita a un monitor.
- El atajo ya está en uso: se avisa y se mantiene el anterior.
- Segunda instancia de la aplicación: se cierra sin abrir ventanas y la primera sigue activa.
- Carpeta de capturas borrada, desconectada o sin permisos: guardar avisa, ofrece elegir otra y el editor sigue abierto.
- Un archivo que no es un PNG válido no aparece en la biblioteca.
- Nombre de archivo repetido: se añade « (2)», « (3)»…

## 13. Criterios de aceptación

- [x] La selección aparece en menos de 200 ms en el equipo de prueba (medido: 191 ms la primera vez tras iniciar, 60–80 ms después).
- [x] Copiar o guardar requiere como máximo una acción tras editar.
- [x] Captio no genera ningún tráfico de red.
- [x] La censura reemplaza los píxeles antes de copiar o guardar.
- [x] Cancelar no deja archivos ni entradas en la biblioteca.
- [x] La biblioteca persiste tras reiniciar.
- [x] La carpeta predeterminada es `FOLDERID_Screenshots`; no se crea ninguna carpeta `Captio` dentro de Imágenes.
- [x] Mover capturas nunca borra un original antes de verificar su copia.
- [x] El inicio automático refleja el estado real del sistema.
- [x] Guardar una copia al copiar usa los mismos bytes y no deshace el portapapeles si falla.
- [x] El repositorio pasa la comprobación de términos prohibidos.
