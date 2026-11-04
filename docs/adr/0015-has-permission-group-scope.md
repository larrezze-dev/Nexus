# ADR-0015 · Cobertura de alcance `GROUP` en `access.has_permission` (ACC-R4)

- **Estado:** Aceptado
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

## Implementación
Implementado en la rama `spec/adr-0015-group-scope`: `R__access__scope_functions.sql` (nueva firma de 6 argumentos, `REVOKE`/`GRANT` actualizados), `domain/access.md §4`, `PermissionTarget.GroupId`, `RoleAssignmentScopeResolver` (destino `GROUP` → solo `p_group_id`) y `AccessPermissionChecker` (sexto argumento). Ya no hay fail-closed para `GROUP`.

Dos detalles que el texto de la decisión no recogía y que hubo que añadir:
- **Cláusula "sin alcance":** `(t.le IS NULL AND t.bu IS NULL AND t.wh IS NULL AND t.prj IS NULL)` también exige `p_group_id IS NULL`. Sin eso, la llamada para un destino `GROUP` (solo `p_group_id`, el resto `NULL`) caería en la forma de listado de ACC-R10 ("el permiso en cualquier alcance") y cualquier titular de `ROLE_ASSIGNMENT:CREATE` en un LE o BU cubriría todos los grupos. Lo cubren `Grant_GroupOnlyTarget_IsNotTreatedAsAnyScope` y `HasPermission_WithOnlyPGroupId_IsNotTheAnyScopeForm`; sin la guarda fallan, además, `Grant_LegalEntityAdminDoesNotCoverItsGroupOrGlobal` y `Grant_GroupAdminDoesNotCoverAnotherGroupOrGlobal`.
- **Sobrecarga anterior:** `CREATE OR REPLACE` con otra lista de parámetros crea una segunda sobrecarga, así que la migración repetible hace antes `DROP FUNCTION IF EXISTS access.has_permission(text, uuid, uuid, uuid, uuid)`. `HasPermission_HasExactlyOneOverload_TheSixArgumentOne` confirma que una base recién migrada tiene exactamente una sobrecarga; como esa base nunca tuvo la firma antigua, no prueba por sí sola la ruta de actualización real (una base que ya corrió la versión de 5 argumentos). Esa ruta se verificó a mano, dos veces de forma independiente, contra un contenedor Postgres 17 desechable durante la revisión de este cambio: aplicar el fichero antiguo, luego el nuevo, y reaplicar el nuevo (como haría Flyway con una repetible) deja exactamente una sobrecarga de 6 argumentos; sin el `DROP` sobreviven las dos.
- **Combinación `p_group_id` + `p_legal_entity_id`:** `a.scope_entity_id = coalesce(p_group_id, t.grp)` prioriza `p_group_id` cuando ambos se pasan a la vez, ignorando el grupo real del LE — un llamante que combinara los dos podría hacer que un admin de GROUP G2 cubriera un LE del GROUP G1. Hoy no es explotable: el único código que construye `GroupId` es `RoleAssignmentScopeResolver` (caso `GROUP`, que solo pasa `p_group_id`, nada más), y `PermissionTarget` es `internal`. Si en el futuro algún llamante pasa ambos a la vez, debe pasar el LE correcto para ese grupo, nunca uno de otro grupo.
