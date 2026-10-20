// Función Spectral propia: nexus-list-get-requires-pagination
//
// Comprueba que toda operación GET cuya respuesta 2xx sea un listado
// (schema `type: array`, o un objeto con propiedad `items`) declare
// paginación por página (page + pageSize) o por cursor (limit + cursor),
// según conventions-api.md §4.
//
// `input` es la operación GET ya resuelta (con $ref expandidos por Spectral).

function isListSchema(schema) {
  if (!schema || typeof schema !== "object") return false;
  if (schema.type === "array") return true;
  if (schema.properties && Object.prototype.hasOwnProperty.call(schema.properties, "items")) {
    return true;
  }
  // allOf/oneOf/anyOf: basta con que una de las ramas sea una lista.
  for (const key of ["allOf", "oneOf", "anyOf"]) {
    if (Array.isArray(schema[key]) && schema[key].some(isListSchema)) return true;
  }
  return false;
}

function responseIsList(responses) {
  if (!responses || typeof responses !== "object") return false;
  return Object.entries(responses).some(([status, response]) => {
    if (!/^2\d\d$/.test(status)) return false;
    const content = response && response.content;
    if (!content || typeof content !== "object") return false;
    return Object.values(content).some((media) => media && isListSchema(media.schema));
  });
}

module.exports = (operation, _opts, context) => {
  if (!operation || typeof operation !== "object") return [];
  if (!responseIsList(operation.responses)) return [];

  const paramNames = (operation.parameters || [])
    .map((parameter) => parameter && parameter.name)
    .filter(Boolean);

  const hasOffsetPagination = paramNames.includes("page") && paramNames.includes("pageSize");
  const hasCursorPagination = paramNames.includes("limit") && paramNames.includes("cursor");

  if (hasOffsetPagination || hasCursorPagination) return [];

  return [
    {
      message:
        "La operación GET devuelve un listado pero no declara paginación: " +
        "añade los parámetros 'page' + 'pageSize' (offset) o 'limit' + 'cursor' (cursor).",
      path: [...context.path, "parameters"],
    },
  ];
};
