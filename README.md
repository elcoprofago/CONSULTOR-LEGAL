# CONSULTOR-LEGAL

App de escritorio (WPF, .NET 8) para hacer consultas avanzadas al servidor MCP de bravo47
(`https://mcp.bravo47.com/v5/mcp`): legislación, jurisprudencia, doctrina, tratados y boletines.

- El formulario se arma solo a partir de las herramientas que publica el servidor.
- El Tribunal de Casación Penal PBA, que bravo47 no trae, lo consulta la app directamente en JUBA
  (`juba.scba.gov.ar`): búsqueda de sumarios por sala y texto completo de los fallos (`JubaTcp.cs`).
- Los resultados se muestran en tarjetas, con botones para encadenar consultas (ficha, leer, artículos, fallos que citan, etc.).
- Navegación por fragmentos y páginas, historial (Atrás), vista JSON, verificación de citas.
- Exportación a TXT o DOCX.
- Consola de log con texto enriquecido, semáforo de recursos (RAM y CPU), luz de conexión con bravo47 y JUBA (roja si alguno no responde), paneles contraíbles, fondo configurable.
- Botón "Buscar actualización" en Configuración: baja e instala la última release de GitHub (`Actualizador.cs`).
- Trae precargado el comando para sumar el servidor a Claude Code.

Compilar: `dotnet build` (requiere el SDK de .NET 8, instalado en el Windows 10).
Publicar una versión: subir `<Version>` en `CONSULTOR-LEGAL.csproj`, commitear y correr `.\publicar.ps1 -Notas "..."`
(compila el `.exe` autocontenido y lo sube a la release `v<Version>` de `elcoprofago/CONSULTOR-LEGAL`).
Configuración del usuario: `%APPDATA%\CONSULTOR-LEGAL\config.json`.
