# ADR-0003 · Núcleo organizativo en Fase 0 (S10)

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** pasos 0.4, 0.6, 1.1; Parte 4 §24–§25

## Contexto
Parte 4 sitúa A6 (RBAC + RLS) en Fase 0 y la Capa 1 en Fase 1. Pero `RoleAssignment.user_id` apunta a `User`, `scope_entity_id` a `LegalEntity`, `BusinessUnit`, `Warehouse` y `Project`, y el alcance se resuelve recorriendo `Group → LegalEntity → BusinessUnit → Warehouse`. Sin esas tablas, A6 no se puede construir ni probar.

## Decisión
- El paso **0.4** crea el núcleo de Capa 1: `Party`, `Group`, `LegalEntity`, `BusinessUnit`, `Warehouse`, `Location` (mínimo), `Currency` (sin tipos de cambio) y `User`, con las columnas que define `domain/org.md` marcadas como "0.4".
- El paso **1.1** completa la Capa 1: geolocalización, tipos de cambio, atributos ERP y pantallas completas.
- `scope_type = PROJECT` existe desde 0.6, pero `access.visible_projects()` solo devuelve resultados útiles cuando existe `project.project` (2.1). Hasta entonces, la FK lógica no se valida.
- Se registra como **S10** en el registro de secuencia.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Mover A6 a Fase 1 | Contradice S4 (cerrado) y deja sin RLS las primeras tablas |
| Construir A6 contra tablas simuladas | Habría que rehacer las funciones de alcance en Fase 1 |

## Consecuencias
- La Fase 0 entrega un sistema con usuarios y Mandanten reales desde el principio.
- La Fase 1 tiene menos contenido en Capa 1.

## Cómo se verifica
Los tests RLS de 0.6 usan `OrgFixture` con dos entidades legales reales.
