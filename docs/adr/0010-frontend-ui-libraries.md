# ADR-0010 · Librerías de UI del frontend

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `conventions-frontend.md`, 0.5 y todos los tracks F

## Contexto
D5 fija Angular. Un back-office con mucha master data y listados grandes necesita una librería de componentes y una tabla de datos con paginación de servidor. Hay que elegir antes de 0.5 para no mezclar estilos.

## Decisión
| Necesidad | Elección | Licencia |
|---|---|---|
| Componentes | Angular Material | MIT |
| Tabla de datos | AG Grid **Community** | MIT |
| i18n en caliente | Transloco | MIT |
| Autenticación | `angular-auth-oidc-client` (OIDC genérico, ADR-0006) | MIT |
| Cliente OpenAPI | `ng-openapi-gen` | MIT |
| Gráficos (ATP, P&L, Gantt complejos) | Apache ECharts + `ngx-echarts` | Apache 2.0 |
| Mock de API | Prism (Stoplight) | Apache 2.0 |
| e2e | Playwright | Apache 2.0 |

AG Grid Enterprise (agrupación, pivot, exportación Excel avanzada) **no** se usa sin ADR.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| PrimeNG | Válido; más componentes, pero estilo menos consistente y cambios frecuentes entre mayores |
| `MatTable` para todo | Insuficiente para listados grandes con orden, filtros y columnas configurables |
| i18n nativo de Angular | Compila un bundle por idioma; no permite cambiar de idioma sin recargar |

## Consecuencias
- Aspecto Material personalizado con tema propio de Nüssli (definido en 0.5).
- Si hiciera falta exportación avanzada a Excel, se haría en servidor.

## Cómo se verifica
`package.json` solo contiene estas dependencias de UI más las del CLI.
