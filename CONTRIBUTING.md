# Contribuir a Captio

Gracias por querer mejorar Captio. Esta guía resume cómo trabajar en el proyecto.

## Antes de empezar

- **Errores:** abre un issue con el formulario *Informe de error*.
- **Ideas:** abre uno con *Solicitud de función* antes de escribir código, para acordar el enfoque.
- **Vulnerabilidades:** nunca en un issue público; sigue [SECURITY.md](SECURITY.md).

La fuente de verdad del comportamiento es [docs/SPEC.md](docs/SPEC.md). Si un cambio altera lo que hace Captio, actualiza la especificación en el mismo pull request.

## Entorno

- Windows 10 u 11 y el SDK de .NET 10.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php), solo para generar el instalador.

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/Captio
```

## Principios que no cambian

- **Sin red:** Captio no hace conexiones. No añadas telemetría, analíticas, actualizaciones automáticas ni servicios remotos.
- **Censura destructiva:** el contenido tapado debe ser irrecuperable en toda imagen que salga de Captio.
- **Ligero en reposo:** nada de temporizadores ni sondeos mientras está en la bandeja.
- **Separación:** la lógica sin interfaz va en `Captio.Core` y se prueba allí; la interfaz WPF va en `Captio`.

## Pull requests

1. Crea una rama desde `main`.
2. Añade o actualiza pruebas. `dotnet test` debe pasar.
3. Si cambias la interfaz, incluye una captura hecha sobre contenido de demostración, nunca sobre tu escritorio real.
4. Describe qué cambia y por qué, y completa la lista de la plantilla.

Las pruebas marcadas con `Category=Desktop` abren ventanas reales y solo se ejecutan en local; el flujo de GitHub las omite.

## Imágenes de la documentación

Las imágenes del README y de la web se generan con el editor real sobre una pantalla de demostración. Si cambias el editor, regenéralas (ver [docs/DESIGN.md](docs/DESIGN.md#imágenes)).

## Licencia

Al contribuir aceptas que tu aportación se publique bajo la [licencia MIT](LICENSE) del proyecto.
