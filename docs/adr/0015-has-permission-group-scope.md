# ADR-0015 · Cobertura de alcance `GROUP` en `access.has_permission` (ACC-R4)

- **Estado:** Propuesto
- **Fecha:** 2026-11-01
- **Decide:** Luciano Arrezze
- **Afecta a:** 0.6 (track B, `POST /v1/role-assignments`), `db/migrations/R__access__scope_functions.sql`, `domain/access.md §4`

## Contexto
ACC-R4: para conceder un rol en un alcance, el que concede necesita `ROLE_ASSIGNMENT:CREATE` en un alcance que **cubra** al de destino (GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY ⊃ BUSINESS_UNIT ⊃ WAREHOUSE). `access.has_permission` no tiene parámetro de grupo: deriva el grupo de `p_legal_entity_id`, y ese mismo LE satisface también la cláusula `LEGAL_ENTITY`. No hay ninguna combinación de argumentos que signifique "cubre el grupo G" sin que una asignación `LEGAL_ENTITY` sobre un LE de G también la cumpla. Pasar "cualquier LE del grupo" permitiría que un `ORG_ADMIN` de un solo LE concediese un rol sobre todo el grupo (escalada de privilegios).

Mientras tanto, el track B (`RoleAssignmentScopeResolver`) aplica **fail-closed**: un destino `GROUP` solo lo cubre una asignación `GLOBAL`. Un administrador con alcance `GROUP` recibe 403 al conceder en su propio grupo.

## Decisión
Propuesta: añadir `p_group_id uuid DEFAULT NULL` a `access.has_permission` (vuelve a Gate C, track C). La cláusula `GROUP` pasa a ser `a.scope_entity_id = coalesce(p_group_id, t.grp)`, y la llamada para un destino `GROUP` pasa solo `p_group_id`. Con eso el LE no se pasa y la cláusula `LEGAL_ENTITY` no puede activarse. El resolver deja de usar el comodín "solo GLOBAL" para `GROUP`.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Pasar un LE cualquiera del grupo | Escalada: un admin de un LE concede sobre todo el grupo |
| Consultar `access.active_assignments()` + `role_permission` desde C# | Duplica la lógica de vigencia de `has_permission`; dos fuentes de verdad |
| Exigir `has_permission` en todos los LE del grupo | Tampoco equivale a GROUP (LE añadidos después) y es O(n) |
| Mantener fail-closed para siempre | Un administrador `GROUP` no puede delegar en su propio grupo |

## Consecuencias
- Se vuelve exacto ACC-R4 para destinos `GROUP`, sin escalada posible.
- Requiere cambiar una función de `db/migrations/` (repetible `R__`) y `domain/access.md §4`: Gate C.
- Hasta que se acepte, la limitación fail-closed queda documentada en el PR de 0.6-B.

## Cómo se verifica
`AccessPermissionCheckerTests`: un `ORG_ADMIN` `GROUP` sobre G recibe `true` para destino `GROUP` G y `false` para otro grupo; un `ORG_ADMIN` `LEGAL_ENTITY` sobre un LE de G sigue recibiendo `false` para `GROUP` G (este último ya existe: `Grant_LegalEntityAdminDoesNotCoverItsGroupOrGlobal`).
