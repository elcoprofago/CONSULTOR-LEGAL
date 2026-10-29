# Tareas del usuario

## Pendientes

- [ ] **3.** Seleccionar una frase de un resultado y pulsar "Verificar cita seleccionada".
- [ ] **9.** Cerrar CONSULTOR-LEGAL y avisarme para recompilar (la app abierta bloquea el `.exe`). Después, abrir el texto de un fallo con varios fragmentos, escribir una palabra en el campo nuevo de arriba del panel y pulsar "Buscar en el resultado" (o Enter): tiene que listar los fragmentos donde aparece, con el contexto resaltado, y "Ir al fragmento" tiene que abrir ese fragmento con la palabra resaltada. En una lista sin fragmentos (por ejemplo, una búsqueda de normas), la misma búsqueda tiene que dejar visibles solo las tarjetas que la contienen. Con el campo vacío se vuelve a ver todo.
- [ ] **10.** En el menú de áreas elegir "Tribunal de Casación Penal PBA (TCPBA)" → "Buscar fallos (JUBA)", buscar por ejemplo `legitima defensa` (con Sala "Todas" y después con una sala) y comprobar que los resultados coinciden con los que da JUBA en el navegador. En una tarjeta pulsar "Leer texto" y pasar fragmentos con ◀ ▶. Anotar acá cualquier diferencia con JUBA.
- [ ] **12.** Abrir la app y mirar la luz "RED" (donde antes estaba GPU): con conexión tiene que quedar apagada (rojo oscuro) y decir "En línea". Desconectar internet y esperar hasta 30 segundos: tiene que titilar en rojo y decir "Sin bravo47 ni JUBA". Al volver la conexión, en 30 segundos se apaga sola.
- [ ] **13.** Bajar `CONSULTOR-LEGAL.exe` de https://github.com/elcoprofago/CONSULTOR-LEGAL/releases/latest, guardarlo en una carpeta propia y abrirlo. En ⚙ → "Actualización" tiene que decir "Versión instalada: 1.0.0"; pulsar "Buscar actualización": tiene que responder "Ya tenés la última versión". Usar desde ahora ese `.exe` (cuando salga una versión nueva, ese mismo botón la instala).
- [ ] **14.** Abrir https://www.virustotal.com/gui/file/35DF5EF9E181DFECA29D667A3A945BEEA05FC8FFFD1F34FE4CE082A0DD082D1A (es la huella SHA-256 del `CONSULTOR-LEGAL.exe` de la release v1.0.0). Si dice "Item not found", subir ese `.exe` (bajado de https://github.com/elcoprofago/CONSULTOR-LEGAL/releases/tag/v1.0.0) en https://www.virustotal.com, pestaña "File", y esperar a que termine. Anotar acá cuántos motores lo marcan y cuáles (en particular si aparece "Microsoft"), y pegar el enlace del informe.
- [ ] **15.** Solo si en la 14 lo marca Microsoft (o Windows Defender lo bloquea al abrirlo): pedir el desmarcado en https://www.microsoft.com/en-us/wdsi/filesubmission → "Software developer", con tu cuenta Microsoft, adjuntando el mismo `.exe` y eligiendo "Incorrectly detected as malware/malicious". Anotar acá el número de envío que te da Microsoft.

## Hecho

- [x] **11.** En "Tribunal de Casación Penal PBA (TCPBA)" → "Buscar fallos (JUBA)", dejar vacío el texto y escribir `57099` en "Nro. de causa": tienen que salir 2 sumarios del fallo de la Sala I (TC0001 LP 57099). Después, en "Leer texto del fallo", escribir `82900` en "Id JUBA" y comprobar que abre el texto de ese fallo. — 29/10/2026, OK: la búsqueda por número de causa funciona (el campo de sumario se reemplazó por el de causa).

- [x] **1.** Abrir `bin\Debug\net8.0-windows\CONSULTOR-LEGAL.exe`, buscar normas con "contrato de trabajo" y probar los botones de una tarjeta (Ficha, Artículos…, Fallos que la citan). — 29/10/2026, sin problemas reportados.
- [x] **2.** Probar una herramienta de jurisprudencia (área Jurisprudencia) y pasar fragmentos con las flechas ◀ ▶ de "Fragmento". — 29/10/2026, OK.
- [x] **4.** Exportar un resultado en TXT y en DOCX y abrir los dos archivos. — 29/10/2026, OK.
- [x] **5.** En la rueda (⚙) elegir "Con textura", cargar una imagen y comprobar que el fondo cambia. — 29/10/2026, OK.
- [x] **6.** Pulsar "Desacoplar" en el Log, mover la ventana, cerrar la app y volver a abrirla: el log tiene que aparecer donde quedó. — 29/10/2026, OK.
- [x] **7.** Hacer una consulta y pulsar "Limpiar resultados": el panel tiene que quedar vacío y los botones Atrás, Fragmento y Página desactivados. — 29/10/2026, OK.
- [x] **8.** Buscar fallos con voces (por ejemplo "SENTENCIA DEFINITIVA"): en las tarjetas, esas voces y las palabras del texto buscado tienen que verse resaltadas en amarillo. — 29/10/2026, OK.
