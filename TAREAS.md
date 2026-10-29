# Tareas del usuario

## Pendientes

- [ ] **3.** Seleccionar una frase de un resultado y pulsar "Verificar cita seleccionada".
- [ ] **9.** Cerrar CONSULTOR-LEGAL y avisarme para recompilar (la app abierta bloquea el `.exe`). Después, abrir el texto de un fallo con varios fragmentos, escribir una palabra en el campo nuevo de arriba del panel y pulsar "Buscar en el resultado" (o Enter): tiene que listar los fragmentos donde aparece, con el contexto resaltado, y "Ir al fragmento" tiene que abrir ese fragmento con la palabra resaltada. En una lista sin fragmentos (por ejemplo, una búsqueda de normas), la misma búsqueda tiene que dejar visibles solo las tarjetas que la contienen. Con el campo vacío se vuelve a ver todo.
- [ ] **10.** En el menú de áreas elegir "Tribunal de Casación Penal PBA (TCPBA)" → "Buscar fallos (JUBA)", buscar por ejemplo `legitima defensa` (con Sala "Todas" y después con una sala) y comprobar que los resultados coinciden con los que da JUBA en el navegador. En una tarjeta pulsar "Leer texto" y pasar fragmentos con ◀ ▶. Anotar acá cualquier diferencia con JUBA.
- [ ] **11.** En "Tribunal de Casación Penal PBA (TCPBA)" → "Buscar fallos (JUBA)", dejar vacío el texto y escribir `57099` en "Nro. de causa": tienen que salir 2 sumarios del fallo de la Sala I (TC0001 LP 57099). Después, en "Leer texto del fallo", escribir `82900` en "Id JUBA" y comprobar que abre el texto de ese fallo.

## Hecho

- [x] **1.** Abrir `bin\Debug\net8.0-windows\CONSULTOR-LEGAL.exe`, buscar normas con "contrato de trabajo" y probar los botones de una tarjeta (Ficha, Artículos…, Fallos que la citan). — 29/10/2026, sin problemas reportados.
- [x] **2.** Probar una herramienta de jurisprudencia (área Jurisprudencia) y pasar fragmentos con las flechas ◀ ▶ de "Fragmento". — 29/10/2026, OK.
- [x] **4.** Exportar un resultado en TXT y en DOCX y abrir los dos archivos. — 29/10/2026, OK.
- [x] **5.** En la rueda (⚙) elegir "Con textura", cargar una imagen y comprobar que el fondo cambia. — 29/10/2026, OK.
- [x] **6.** Pulsar "Desacoplar" en el Log, mover la ventana, cerrar la app y volver a abrirla: el log tiene que aparecer donde quedó. — 29/10/2026, OK.
- [x] **7.** Hacer una consulta y pulsar "Limpiar resultados": el panel tiene que quedar vacío y los botones Atrás, Fragmento y Página desactivados. — 29/10/2026, OK.
- [x] **8.** Buscar fallos con voces (por ejemplo "SENTENCIA DEFINITIVA"): en las tarjetas, esas voces y las palabras del texto buscado tienen que verse resaltadas en amarillo. — 29/10/2026, OK.
