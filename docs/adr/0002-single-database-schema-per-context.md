# ADR-0002 · Una base PostgreSQL, un esquema por contexto

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `architecture.md §2–§3`, `conventions-database.md §2`, todos los pasos

## Contexto
D1 fija microservicios .NET. A6 exige RLS nativo en PostgreSQL sobre toda tabla multi-Mandant, con consistencia transaccional. Si cada servicio tuviera su propia base, el alcance de acceso tendría que replicarse por eventos en cada una. Esa réplica es asíncrona, así que una revocación tardaría en aplicarse (el mismo problema de RC6).

## Decisión
- Una sola base `nexus`. Cada bounded context tiene su esquema.
- Cada servicio desplegable tiene su rol (`svc_<servicio>`) con DML solo en sus esquemas.
- `org`, `uom` y `catalog` son esquemas de **referencia**: cualquier servicio los lee y puede tener FK hacia ellos.
- `access` solo se consulta a través de funciones `SECURITY DEFINER`.
- Entre esquemas transaccionales no hay FK ni lecturas cruzadas: se comunican por eventos (ARC-R1…R4).
- Agrupación inicial de contextos en servicios: `architecture.md §2`.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Una base por servicio | RLS necesitaría el alcance replicado en cada base, con retraso en revocaciones |
| Base compartida sin esquemas ni roles | Acoplamiento total; nada impide que un servicio escriba en tablas ajenas |
| Monolito modular | Contradice D1 |

## Consecuencias
- RLS es transaccional y uniforme en todos los servicios.
- La base es un punto único: se mitiga con alta disponibilidad de Azure Flexible Server y réplicas de lectura (0.11).
- Separar un contexto a otra base en el futuro es posible, porque no hay FK entre esquemas transaccionales; el coste es replicar `org`/`catalog` y el alcance.

## Cómo se verifica
Test de integración por servicio: su rol no puede `INSERT` en un esquema ajeno. Consulta en CI: ninguna FK entre dos esquemas transaccionales (`pg_constraint`).
