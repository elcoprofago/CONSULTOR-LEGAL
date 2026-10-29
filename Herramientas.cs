using System.Text.Json.Nodes;

namespace ConsultorLegal
{
    public enum TipoCampo { Texto, Entero, Booleano, Opciones, TextoLargo, Cuestiones }

    // Un control del formulario. Nombre es la ruta del argumento: "query", o
    // "a.ref" para los campos de un objeto anidado (comparar_textos).
    public class Campo
    {
        public string Nombre { get; set; } = "";
        public string Etiqueta { get; set; } = "";
        public TipoCampo Tipo { get; set; }
        public bool Requerido { get; set; }
        public JsonNode? PorDefecto { get; set; }
        public List<string> Opciones { get; set; } = new();     // enum cerrado
        public List<string> Sugerencias { get; set; } = new();  // combo editable
        public string? Ayuda { get; set; }
        public string? ValorFijo { get; set; }                  // "const" del esquema: no se muestra
        public bool EsFecha { get; set; }
    }

    public class Herramienta
    {
        public string Nombre { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Grupo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public List<Campo> Campos { get; set; } = new();
        public override string ToString() => Titulo;
    }

    // Lo que el servidor no dice en español: grupos, nombres, etiquetas y
    // valores sugeridos (probados contra el servidor el 29/10/2026). Los
    // campos salen igual del inputSchema de tools/list, así que una
    // herramienta o un parámetro nuevo aparece aunque no esté acá, con su
    // nombre crudo y en el grupo "Otras".
    public static class Herramientas
    {
        public static readonly string[] OrdenGrupos =
        {
            "Legislación", "Corte Suprema (CSJN)", "Suprema Corte PBA (SCBA)", JubaTcp.Grupo, "Tratados",
            "Boletines oficiales", "Tribunales y funcionarios", "Verificación", "Servidor", "Otras",
        };

        private static readonly Dictionary<string, (string grupo, string titulo)> Info = new()
        {
            ["buscar_normas"] = ("Legislación", "Buscar normas"),
            ["obtener_norma"] = ("Legislación", "Ficha de una norma"),
            ["leer_articulos"] = ("Legislación", "Leer artículos"),
            ["buscar_articulos"] = ("Legislación", "Buscar dentro de artículos"),
            ["modificaciones"] = ("Legislación", "Modificaciones (modifica / modificada por)"),
            ["vigencia"] = ("Legislación", "Texto vigente a una fecha"),
            ["comparar_textos"] = ("Legislación", "Comparar dos versiones de un artículo"),
            ["buscar_fallos"] = ("Corte Suprema (CSJN)", "Buscar fallos"),
            ["obtener_fallo"] = ("Corte Suprema (CSJN)", "Ficha de un fallo"),
            ["leer_fallo"] = ("Corte Suprema (CSJN)", "Leer texto del fallo"),
            ["fallos_que_citan"] = ("Corte Suprema (CSJN)", "Fallos que citan una norma"),
            ["red_precedentes"] = ("Corte Suprema (CSJN)", "Red de precedentes"),
            ["fallos_relacionados"] = ("Corte Suprema (CSJN)", "Fallos relacionados"),
            ["fallos_por_ministro"] = ("Corte Suprema (CSJN)", "Votos de un ministro"),
            ["predecir_criterio_csjn"] = ("Corte Suprema (CSJN)", "Anticipar criterio de la Corte"),
            ["buscar_fallos_scba"] = ("Suprema Corte PBA (SCBA)", "Buscar fallos (JUBA)"),
            ["obtener_fallo_scba"] = ("Suprema Corte PBA (SCBA)", "Ficha de un fallo"),
            ["leer_fallo_scba"] = ("Suprema Corte PBA (SCBA)", "Leer texto del fallo"),
            ["buscar_tratados"] = ("Tratados", "Buscar tratados"),
            ["obtener_tratado"] = ("Tratados", "Ficha de un tratado"),
            ["leer_tratado"] = ("Tratados", "Leer texto del tratado"),
            ["buscar_bora"] = ("Boletines oficiales", "Boletín Oficial de la Nación"),
            ["leer_aviso_bora"] = ("Boletines oficiales", "Leer aviso del Boletín Nacional"),
            ["buscar_boletin"] = ("Boletines oficiales", "Boletín provincial (PBA)"),
            ["leer_aviso_boletin"] = ("Boletines oficiales", "Leer aviso del Boletín provincial"),
            ["buscar_tribunales"] = ("Tribunales y funcionarios", "Buscar tribunales"),
            ["obtener_tribunal"] = ("Tribunales y funcionarios", "Ficha de un tribunal"),
            ["buscar_funcionarios"] = ("Tribunales y funcionarios", "Buscar jueces y funcionarios"),
            ["verificar_cita"] = ("Verificación", "Verificar una cita textual"),
            ["estado_corpus"] = ("Servidor", "Estado y cobertura del corpus"),
            ["version"] = ("Servidor", "Versión del servidor"),
        };

        private static readonly Dictionary<string, string> Etiquetas = new()
        {
            ["query"] = "Texto a buscar",
            ["ref"] = "Referencia",
            ["tipo"] = "Tipo",
            ["tipo_norma"] = "Tipo de norma",
            ["organismo"] = "Organismo",
            ["fecha_desde"] = "Fecha desde",
            ["fecha_hasta"] = "Fecha hasta",
            ["desde"] = "Desde",
            ["hasta"] = "Hasta",
            ["fecha"] = "Fecha",
            ["limit"] = "Cantidad máxima",
            ["offset"] = "Saltear los primeros",
            ["jurisdiccion"] = "Jurisdicción",
            ["articulos"] = "Artículos",
            ["articulo"] = "Artículo",
            ["documento"] = "Versión del texto",
            ["fragmento"] = "Fragmento n.º",
            ["direccion"] = "Dirección",
            ["anio_desde"] = "Año desde",
            ["anio_hasta"] = "Año hasta",
            ["voces"] = "Voces (descriptores)",
            ["id_fallo"] = "Id del fallo",
            ["id_juba"] = "Id JUBA",
            ["ministro"] = "Ministro",
            ["tipo_voto"] = "Tipo de voto",
            ["problema"] = "Problema a resolver",
            ["cuestiones"] = "Cuestiones (una por línea)",
            ["min_sumarios"] = "Mínimo de sumarios",
            ["top_k"] = "Fallos por cuestión",
            ["incluir_alineamientos"] = "Incluir alineamientos de ministros",
            ["tribunal"] = "Tribunal",
            ["materia"] = "Materia",
            ["tipo_fallo"] = "Tipo de fallo",
            ["solo_con_texto"] = "Solo con texto completo",
            ["catalogo"] = "Catálogo",
            ["contraparte"] = "País o contraparte",
            ["estado_tratado"] = "Estado",
            ["firmado_desde"] = "Firmado desde",
            ["firmado_hasta"] = "Firmado hasta",
            ["solo_jerarquia_constitucional"] = "Solo con jerarquía constitucional",
            ["caso"] = "Formato del texto",
            ["seccion"] = "Sección",
            ["rubro"] = "Rubro",
            ["solo_frescas"] = "Solo publicaciones recientes",
            ["solo_destacadas"] = "Solo destacadas",
            ["id_publicacion"] = "Id del aviso",
            ["aviso_id"] = "Id del aviso",
            ["fuero"] = "Fuero",
            ["localidad"] = "Localidad",
            ["instancia"] = "Instancia",
            ["circunscripcion"] = "Circunscripción / departamento",
            ["nombre"] = "Nombre",
            ["cargo"] = "Cargo",
            ["texto"] = "Texto de la cita",
            ["modo"] = "Modo",
            ["granularidad"] = "Comparar por",
            ["a"] = "Texto A",
            ["b"] = "Texto B",
        };

        private static readonly string[] Provincias =
        {
            "caba", "pba", "catamarca", "chaco", "chubut", "cordoba", "corrientes", "entre_rios",
            "formosa", "jujuy", "la_pampa", "la_rioja", "mendoza", "misiones", "neuquen", "rio_negro",
            "salta", "san_juan", "san_luis", "santa_cruz", "santa_fe", "santiago_del_estero",
            "tierra_del_fuego", "tucuman",
        };

        private static readonly string[] TiposNorma =
            { "Ley", "Decreto", "Decreto/Ley", "Resolución", "Disposición", "Decisión Administrativa" };

        private static readonly string[] RefsFrecuentes =
            { "cn", "ccyc", "cp", "lct", "cadh", "ley_20744", "ley_24240", "ley_19549" };

        // Sugerencias por parámetro, con excepciones "herramienta.parámetro".
        private static readonly Dictionary<string, string[]> SugerenciasPorCampo = new()
        {
            ["buscar_normas.jurisdiccion"] = Provincias,
            ["buscar_tribunales.jurisdiccion"] = new[] { "federal", "nacional", "caba", "pba" }
                .Concat(Provincias.Skip(2).Select(p => "provincia_" + p)).ToArray(),
            ["buscar_funcionarios.jurisdiccion"] = new[] { "federal", "nacional", "caba", "pba" }
                .Concat(Provincias.Skip(2).Select(p => "provincia_" + p)).ToArray(),
            ["buscar_boletin.jurisdiccion"] = new[] { "pba" },
            ["buscar_normas.tipo"] = TiposNorma,
            ["buscar_bora.tipo"] = new[] { "ley", "decreto", "resolucion", "disposicion", "decision_administrativa", "aviso" },
            ["buscar_tribunales.tipo"] = new[] { "juzgado", "tribunal", "camara", "fiscalia", "defensoria" },
            ["buscar_tratados.tipo"] = new[] { "Bilaterales con Países", "Multilaterales" },
            ["buscar_bora.seccion"] = new[] { "primera", "tercera" },
            ["buscar_boletin.seccion"] = new[] { "oficial", "judicial", "jurisprudencia" },
            ["tipo_norma"] = TiposNorma,
            ["ref"] = RefsFrecuentes,
            ["ministro"] = new[] { "ROSATTI", "ROSENKRANTZ", "LORENZETTI", "GARCÍA-MANSILLA", "MAQUEDA",
                                   "HIGHTON de NOLASCO", "FAYT", "PETRACCHI", "ZAFFARONI", "ARGIBAY" },
            ["cargo"] = new[] { "juez", "ministro", "presidente", "vicepresidente", "vocal", "fiscal", "defensor", "secretario" },
            ["materia"] = new[] { "Civil y Comercial", "Laboral", "Penal", "Contencioso Administrativa" },
            ["buscar_tratados.materia"] = new[] { "Extradición", "Comercio", "Inversiones", "Derechos Humanos", "Cooperación" },
            ["tipo_fallo"] = new[] { "Sentencia Definitiva", "Interlocutoria" },
            ["estado_tratado"] = new[] { "Vigente", "En vigor", "No está en vigor", "Extinguido" },
            ["rubro"] = new[] { "DECRETOS", "RESOLUCIONES", "DISPOSICIONES", "EDICTOS" },
            ["instancia"] = new[] { "primera", "segunda", "unica" },
            ["fuero"] = new[] { "Civil y Comercial", "Laboral", "Penal", "Familia", "Contencioso Administrativo", "Federal" },
            ["modo"] = new[] { "estricta", "tolerante" },
        };

        private static readonly Dictionary<string, string> Ayudas = new()
        {
            ["ref"] = "Id canónico (ley_20744), alias (cn, ccyc, lct, cp, cadh) o id numérico de InfoLEG.",
            ["articulos"] = "Uno, varios o un rango: 245 · 240-250 · 14 bis, 245",
            ["fecha"] = "Formato AAAA-MM-DD",
            ["jurisdiccion"] = "Vacío = corpus nacional. Atajos: caba, pba, cordoba… o provincia_<nombre>.",
            ["voces"] = "Descriptores temáticos de los sumarios de la Corte, por ejemplo: ACCION DE AMPARO",
            ["cuestiones"] = "Una cuestión por línea:  etiqueta | texto a buscar | voces | normas separadas por coma\n" +
                             "Solo los dos primeros son obligatorios. Ejemplo:\n" +
                             "legitimación | legitimación activa asociaciones | ACCION DE AMPARO | cn",
            ["texto"] = "Pegá la cita textual (30 caracteres como mínimo). Conviene indicar también la referencia.",
            ["offset"] = "Para paginar: usá los botones ◀ ▶ de la barra de resultados.",
        };

        public static Herramienta Desde(JsonObject tool)
        {
            string nombre = tool["name"]?.GetValue<string>() ?? "";
            var (grupo, titulo) = Info.TryGetValue(nombre, out var i) ? i : ("Otras", nombre);
            var h = new Herramienta
            {
                Nombre = nombre,
                Titulo = titulo,
                Grupo = grupo,
                Descripcion = Normalizar(tool["description"]?.GetValue<string>() ?? ""),
            };
            var esquema = tool["inputSchema"] as JsonObject;
            if (esquema != null) AgregarCampos(h, esquema, esquema, "");
            return h;
        }

        private static void AgregarCampos(Herramienta h, JsonObject raiz, JsonObject obj, string prefijo)
        {
            var requeridos = (obj["required"] as JsonArray)?.Select(n => n?.GetValue<string>()).ToHashSet() ?? new();
            if (obj["properties"] is not JsonObject props) return;
            foreach (var (nombre, nodo) in props)
            {
                if (nodo is not JsonObject p) continue;
                string ruta = prefijo + nombre;

                if (Referencia(raiz, p) is JsonObject sub && sub["properties"] != null)
                {
                    // Objeto anidado: sus campos van con el prefijo "a.", "b.".
                    string etiquetaGrupo = Etiquetas.GetValueOrDefault(nombre, nombre);
                    int antes = h.Campos.Count;
                    AgregarCampos(h, raiz, sub, ruta + ".");
                    foreach (var c in h.Campos.Skip(antes)) c.Etiqueta = $"{etiquetaGrupo} · {c.Etiqueta}";
                    continue;
                }

                var campo = new Campo
                {
                    Nombre = ruta,
                    Etiqueta = Etiquetas.GetValueOrDefault(nombre, p["title"]?.GetValue<string>() ?? nombre),
                    Requerido = requeridos.Contains(nombre),
                    PorDefecto = p["default"]?.DeepClone(),
                    Ayuda = Ayudas.TryGetValue(nombre, out var ayuda) ? ayuda : p["description"]?.GetValue<string>(),
                };

                if (p["const"] is JsonNode k) { campo.ValorFijo = k.GetValue<string>(); h.Campos.Add(campo); continue; }

                var (tipo, opciones) = TipoBase(raiz, p);
                if (tipo == "array" && nombre == "cuestiones") campo.Tipo = TipoCampo.Cuestiones;
                else if (opciones.Count > 0) { campo.Tipo = TipoCampo.Opciones; campo.Opciones = opciones; }
                else campo.Tipo = tipo switch
                {
                    "integer" or "number" => TipoCampo.Entero,
                    "boolean" => TipoCampo.Booleano,
                    _ => nombre is "problema" or "texto" ? TipoCampo.TextoLargo : TipoCampo.Texto,
                };

                // Las fechas son texto AAAA-MM-DD; los "desde/hasta" enteros son años.
                campo.EsFecha = campo.Tipo == TipoCampo.Texto &&
                                (nombre.Contains("fecha") || nombre is "desde" or "hasta" || nombre.StartsWith("firmado_"));
                if (campo.EsFecha && campo.Ayuda == null) campo.Ayuda = "Formato AAAA-MM-DD";

                if (SugerenciasPorCampo.TryGetValue($"{h.Nombre}.{nombre}", out var s) ||
                    SugerenciasPorCampo.TryGetValue(nombre, out s))
                    campo.Sugerencias = s.ToList();

                h.Campos.Add(campo);
            }
        }

        private static JsonObject? Referencia(JsonObject raiz, JsonObject p)
        {
            string? r = p["$ref"]?.GetValue<string>();
            if (r == null || !r.StartsWith("#/$defs/")) return null;
            return raiz["$defs"]?[r["#/$defs/".Length..]] as JsonObject;
        }

        // Saca el tipo real de un "anyOf: [X, null]" (los opcionales de pydantic).
        private static (string tipo, List<string> opciones) TipoBase(JsonObject raiz, JsonObject p)
        {
            if (p["anyOf"] is JsonArray alternativas)
                foreach (var a in alternativas)
                    if (a is JsonObject o && o["type"]?.GetValue<string>() != "null")
                        return TipoBase(raiz, o);
            var opciones = (p["enum"] as JsonArray)?.Select(n => n?.ToString() ?? "").ToList() ?? new();
            return (p["type"]?.GetValue<string>() ?? "string", opciones);
        }

        // Las descripciones vienen con la sangría del docstring de Python.
        private static string Normalizar(string s)
        {
            var parrafos = s.Replace("\r", "").Split("\n\n");
            return string.Join("\n\n", parrafos.Select(par =>
                string.Join(" ", par.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))));
        }
    }
}
