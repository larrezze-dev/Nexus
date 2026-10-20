// Función Spectral propia: nexus-response-2xx-example-required
//
// Comprueba que toda respuesta 2xx tenga al menos un ejemplo, ya sea a nivel
// de media type (`content.<tipo>.example` o `content.<tipo>.examples`) o a
// nivel de schema (`content.<tipo>.schema.example`), según conventions-api.md §8
// (es lo que ve el frontend contra el mock Prism mientras no hay backend real).
//
// `input` es el objeto `responses` de una operación, ya resuelto ($ref expandidos).

function mediaHasExample(media) {
  if (!media || typeof media !== "object") return false;
  if (media.example !== undefined) return true;
  if (media.examples && Object.keys(media.examples).length > 0) return true;
  if (media.schema && media.schema.example !== undefined) return true;
  return false;
}

module.exports = (responses, _opts, context) => {
  if (!responses || typeof responses !== "object") return [];

  const errors = [];

  for (const [status, response] of Object.entries(responses)) {
    if (!/^2\d\d$/.test(status)) continue;

    const content = response && response.content;
    const hasExample =
      content && typeof content === "object" && Object.values(content).some(mediaHasExample);

    if (!hasExample) {
      errors.push({
        message: `La respuesta ${status} no tiene ningún ejemplo (content.example, content.examples o schema.example).`,
        path: [...context.path, status],
      });
    }
  }

  return errors;
};
