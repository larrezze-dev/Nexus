# ADR-0004 · `EffectiveAccessScope` evaluado por sentencia, sin materializar

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `domain/access.md`, paso 0.6; A6, A7 (tabla de vistas derivadas)

## Contexto
A7 pone `EffectiveAccessScope` como vista materializada en PostgreSQL, refrescada con el evento `RoleAssignment.changed`. Eso tiene dos agujeros:

1. **Revocación con retraso.** Si el refresco va por Kafka, entre la revocación y el refresco el usuario sigue viendo filas. Choca con "RLS exige consistencia transaccional" del propio A7.
2. **Caducidad sin evento.** El `FIELD_CAPTURE` auto-provisionado (P6) caduca por `valid_to`. El paso del tiempo no emite ningún evento, así que el acceso sobrevive a su fecha de fin hasta el siguiente refresco.

## Decisión
- No hay vista materializada. El alcance se calcula con funciones `STABLE SECURITY DEFINER` sobre `access.role_assignment` y la jerarquía de `org`:
  `access.visible_legal_entities()`, `access.visible_business_units()`, `access.visible_warehouses()`, `access.visible_projects()`, `access.has_permission(...)`.
- Cada función filtra las asignaciones vigentes **en el momento de la consulta**: `valid_from <= now() AND (valid_to IS NULL OR valid_to > now())`.
- Las políticas las invocan envueltas en `(SELECT …)` para que se evalúen una vez por sentencia (InitPlan), no por fila.
- Se mantiene `access.effective_access_scope` como **vista normal** (no materializada), para inspección y para la pantalla "¿qué ve este usuario?".
- El evento `nexus.platform.role-assignment.changed` se sigue publicando, para consumidores externos y auditoría, no para la seguridad.
- Si una medición demuestra que el coste es inaceptable, la alternativa es una tabla de alcance mantenida **en la misma transacción** que el `RoleAssignment`, nunca por evento.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Vista materializada refrescada por evento (A7 literal) | Agujeros 1 y 2 |
| Vista materializada refrescada en la misma transacción | `REFRESH` completo en cada cambio de permisos; bloqueos y coste creciente |
| Tabla de alcance precalculada en la transacción | Más compleja; queda como plan B medido |

## Consecuencias
- Revocaciones y caducidades aplican en la siguiente sentencia.
- Coste por sentencia: unas pocas consultas indexadas sobre `role_assignment` y `org`. Se mide en 0.6 (objetivo: < 2 ms por llamada con 10 000 asignaciones).

## Cómo se verifica
Tests de 0.6: (a) revocar y consultar en la transacción siguiente → no ve; (b) asignación con `valid_to` = ahora + 2 s, esperar 3 s → no ve; (c) benchmark con 10 000 asignaciones.
