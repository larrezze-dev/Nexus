# NEXUS — Equipo de agentes

Referencia rápida del equipo de subagentes definido en `.claude/agents/` y en `CLAUDE.md §7`. La sesión principal orquesta: descompone cada track en subtareas, fija el nivel de cada una y delega en el subagente correspondiente. Solo escribe código directamente cuando una subtarea de nivel 3 no se puede delegar.

## Tabla de agentes

| Agente | Nivel | Modelo | Herramientas | Tareas típicas | Skills asociados |
|---|---|---|---|---|---|
| [`nx-mechanic`](.claude/agents/nx-mechanic.md) | 1 · Mecánico | `haiku` | Read, Grep, Glob, Bash, Edit, Write, Skill | Localizar código o docs · regenerar clientes y clases (`make contracts`) · semillas · claves i18n · ejecutar tests/lint y resumir el resultado · boilerplate derivado 1:1 del contrato (DTO, rutas, esquemas Avro de una tabla ya definida) | `run` |
| [`nx-implementer`](.claude/agents/nx-implementer.md) | 2 · Implementación | `sonnet` | Read, Grep, Glob, Bash, Edit, Write, Skill | Handlers, endpoints, validadores · consumidores y réplicas `*_ref` · pantallas Angular (plantillas de `conventions-frontend.md §4`) · migraciones que copian una DDL ya escrita en `docs/domain/` · tests de reglas `XXX-Rn` | `verify` |
| [`nx-architect`](.claude/agents/nx-architect.md) | 3 · Crítico | `opus` | Read, Grep, Glob, Bash, Edit, Write, Skill | RLS y funciones `access.*` · `LedgerPostingService` y todo lo que escribe `stock_movement`/`stock_balance` · `UnitOfWork`, Outbox y relay · versionado VR1 · Solver (modelo y restricciones) · consulta ATP · concurrencia y bloqueos · dinero, impuestos y conversión de unidades · migraciones que alteran tablas existentes · cualquier decisión no escrita en `docs/` (redacta un ADR `Propuesto` y para) | `verify`, `security-review` |
| [`nx-reviewer`](.claude/agents/nx-reviewer.md) | Revisión | `opus` | Read, Grep, Glob, Bash, Skill *(sin Edit ni Write)* | Revisa todo lo de nivel 3 y una muestra de los niveles 1–2 antes de abrir el PR; nunca revisa su propio trabajo | `code-review`, `security-review` |

## Reglas de uso (CLAUDE.md §7)

1. **El nivel lo fija la subtarea, no el track.** Si una subtarea toca algo de la fila "Crítico" de `nx-architect`, es nivel 3 aunque parezca pequeña.
2. **Escalado:** si un subagente falla dos veces o devuelve tests en rojo, se reasigna al nivel superior. Nunca se baja de nivel una subtarea crítica para ahorrar coste.
3. **Paralelismo:** las subtareas independientes del mismo track se lanzan en paralelo, cada una con `isolation: worktree` si tocan ficheros distintos. Las que comparten ficheros van en secuencia.
4. **Contexto mínimo:** cada subagente recibe solo la subtarea, los ficheros que debe tocar y las secciones de `docs/` que le aplican (de la tabla "Leer" de la ficha), nunca la ficha entera ni el historial de la conversación.
5. **Cierre:** el PR incluye la tabla "Subtarea · Nivel · Subagente · Escalados", para que Luciano vea qué hizo cada modelo.

## Notas

- Las reglas duras de `CLAUDE.md §3` que aplican a cada agente están repetidas dentro de su propio fichero en `.claude/agents/`, para que cada subagente las tenga presentes sin necesitar leer `CLAUDE.md` completo.
- Los skills listados son los que cada agente debe invocar explícitamente con la herramienta `Skill` como parte de su formato de trabajo; no sustituyen las reglas duras ni el checklist de aceptación de cada paso.
