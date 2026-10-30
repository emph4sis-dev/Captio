# Seguridad

## Versiones con soporte

Solo la última versión publicada en [Releases](https://github.com/emph4sis-dev/Captio/releases/latest) recibe correcciones de seguridad.

## Informar de una vulnerabilidad

**No abras un issue público.** Usa el aviso privado de GitHub:

1. Ve a la pestaña [**Security**](https://github.com/emph4sis-dev/Captio/security) del repositorio.
2. Pulsa **Report a vulnerability** y describe el problema, cómo reproducirlo y qué versión usas.

Recibirás respuesta en ese mismo aviso. Si el problema se confirma, se corregirá en una nueva versión y se te dará crédito si lo deseas.

## Qué entra en el alcance

- La aplicación Captio y su instalador.
- Cualquier forma de que una censura deje recuperable el contenido tapado.
- Cualquier escritura en disco o conexión de red que Captio no debería hacer.
- El flujo de publicación de este repositorio.

## Verificar una descarga

Las versiones nuevas incluyen un archivo `.sha256` y una atestación de procedencia generada por GitHub Actions:

```bash
gh attestation verify Captio-Setup-1.0.1.exe --repo emph4sis-dev/Captio
```
