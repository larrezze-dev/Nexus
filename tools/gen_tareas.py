#!/usr/bin/env python3
"""Genera TAREAS.md y TAREAS.html a partir de docs/steps/STATUS.md (CLAUDE.md §10).

Uso: python3 tools/gen_tareas.py
Las descripciones y comentarios editoriales viven en DESC / NOTES más abajo.
"""
import html, json, re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
STATUS = (ROOT / "docs/steps/STATUS.md").read_text(encoding="utf-8")

DESC = {
 "0.1": "Repositorio único, integración continua y esqueletos de todos los proyectos.",
 "0.2": "Entorno local reproducible (contenedores, Keycloak) para desarrollar sin depender de nada externo.",
 "0.3": "Base de datos base: roles, migraciones Flyway, bandeja de salida de eventos (Outbox) y unidad de trabajo.",
 "0.4": "Núcleo organizativo: empresas, almacenes y estructura sobre la que cuelga todo lo demás.",
 "0.5": "Login, gateway de entrada y carcasa de la aplicación web Angular.",
 "0.6": "Roles y permisos (RBAC) y aislamiento de datos por empresa (RLS).",
 "0.7": "Relay que publica eventos a Kafka, registro de esquemas, consumidores y cola de errores.",
 "0.8": "Proyecciones analíticas en ClickHouse y auditoría de cambios.",
 "0.9": "Flujo de aprobaciones reutilizable por el resto de módulos.",
 "0.10": "Gestión documental: adjuntos y documentos asociados a entidades.",
 "0.11": "Infraestructura en Azure como código (Terraform) y conexión Arc.",
 "1.1": "Resto de maestros de la capa 1 (datos organizativos de base).",
 "1.2": "Unidades de medida y conversiones; nace el servicio de catálogo.",
 "1.3": "Taxonomía de material y tipos de activo (qué se puede alquilar/mover).",
 "1.4": "Precios y composiciones (BOM/kits) con historial de versiones.",
 "1.5": "Unidades físicas de material, lotes y proveedores.",
 "1.6": "Planes de mantenimiento de los activos.",
 "2.1": "Proyectos y trabajos (eventos); nace el servicio de operaciones.",
 "2.2": "Líneas de material de cada trabajo y sus partes de entrega.",
 "2.3": "Transportes del trabajo y matriz de costes de transporte.",
 "2.4": "Reglas operacionales que completan los datos de entrada del Solver.",
 "3.1": "Libro mayor de stock (movimientos y saldos): la fuente de verdad de existencias.",
 "3.2": "Pedidos de compra y recepción de mercancía.",
 "3.3": "Inventario físico y ajustes.",
 "4.1": "Ubicaciones, transferencias entre almacenes y registro de operaciones.",
 "4.2": "Reservas de material para trabajos.",
 "4.3": "Disponibilidad prometida (ATP): qué material está libre y cuándo.",
 "4.4": "Solver Timefold (servicio JVM) que optimiza asignación y transferencias.",
 "4.5": "Propuestas del Solver convertidas en transferencias reales.",
 "5.1": "Ofertas: condiciones de pago, oportunidades y presupuestos; nace el servicio comercial.",
 "5.2": "Contratos, enmiendas y creación de proyectos desde contrato.",
 "5.3": "Trabajadores y maestros de RR.HH.; nace el servicio de recursos.",
 "5.4": "Planificación de personal, horas y gastos (captura de campo).",
 "5.5": "Facturación y exportación al ERP.",
 "5.6": "Catálogo de costes y presupuesto de proyecto; nace el servicio de controlling.",
 "5.7": "Costes reales y cuenta de resultados (P&L) por proyecto.",
 "6.1": "Identificadores de activos (códigos, RFID), sesiones de carga y escaneos.",
 "6.2": "Puesta en marcha (commissioning) de instalaciones.",
 "6.3": "Trazabilidad del material en campo.",
 "6.4": "Base de la app móvil: offline-first y sincronización.",
 "6.5": "Flujos de la app móvil y lectura RFID.",
 "7.1": "Protección de datos personales (GDPR/PII).",
 "7.2": "ETL: extracción de EasyJob/adunic a staging.",
 "7.3": "ETL: transformación, carga, mapa de ids y reconciliación.",
 "7.4": "Ensayos de migración, runbook y decisión go/no-go del cutover.",
}

NOTES = {
 "0.3": "Aprobado 2026-11-06.",
 "0.4": "Aprobado 2026-10-30.",
 "0.6": "Aprobado 2026-11-06 (5 PRs, 2 ADRs, 2 vulnerabilidades reales corregidas).",
 "0.7": "Aprobado 2026-11-08 (C: PR #25, #27, #30 · S: PR #28 · B: PR #29, #31 · Gate D: PR #32; ADR-0017 y ADR-0018 aceptados).",
 "0.8": "C entregado (contrato ClickHouse + auditoría, AUDIT:READ, PR #34); S entregado (nexus-projections, BuildingBlocks.Analytics, endpoint de auditoría, PR #35); ADR-0019 implementado (C: columnas aggregate_version + 409 en updateRolePermissions, PR #37; B: versión persistida como token de concurrencia, PR #38; verificado en vivo: auditoría 3, 2, 1). Sigue F y Gate D; en entornos con datos: rebuild dim_legal_entity y dim_warehouse.",
 "0.1": "Aprobado 2026-11-11.",
 "0.2": "Aprobado 2026-11-11.",
 "0.5": "Aprobado 2026-11-11 (C: PR #12 · B: PR #13 · F: PR #14). Deuda: e2e 0.5-login.spec.ts pasa a 1.3; deuda menor del PR #14 anotada en STATUS.",
 "0.9": "WF-Q1 y WF-Q2 decididos el 2026-11-11 (auto-aprobación opcional por plantilla; sin plantilla, rechazo). Listo para lanzar.",
 "1.1": "Espera a que Luciano acepte ADR-0020 (versión de agregado de org.exchange_rate).",
 "1.4": "CAT-Q1 y VER-Q1 decididos el 2026-11-11 (fecha futura en todas las VR1, el futuro es editable); espera a ADR-0020.",
 "1.5": "CAT-Q4 decidido el 2026-11-11: asset_batch es tabla propia.",
 "4.4": "Depende del hito S5 (aprobar 2.4) y del contrato 4.3-C.",
 "7.2": "Requiere acceso a NUS-SQL-001\\NRP.",
}

# ---------- parseo de STATUS.md ----------
phases, steps, cur = [], [], None
for line in STATUS.splitlines():
    m = re.match(r"## (Fase \d) · (.+)", line)
    if m:
        cur = {"id": m.group(1), "name": m.group(2).strip()}
        phases.append(cur)
        continue
    m = re.match(r"\| \[(\d+\.\d+)\]\(([^)]+)\) \| (.+?) \| (.+?) \| (.+?) \| (.+?) \| (.+?) \| (.+?) \|\s*(.*?)\s*\|$", line)
    if m and cur:
        sid, link, title, tracks, dep, par, blk, st, date = m.groups()
        letters = sorted(set(re.findall(r"[CBSFIMJ]", re.sub(r"\(.*?\)", "", tracks.replace("-", " ")))), key="CBSFIMJ".index)
        deps = re.findall(r"\d+\.\d+", dep)
        rng = re.search(r"(\d+)\.(\d+)–(\d+)\.(\d+)", dep)
        if rng and rng.group(1) == rng.group(3):
            deps = [f"{rng.group(1)}.{i}" for i in range(int(rng.group(2)), int(rng.group(4)) + 1)]
        if "hito S5" in dep:
            deps = ["2.4"] + deps
        steps.append(dict(id=sid, phase=cur["id"], title=title.strip(), tracks=letters, tracks_raw=tracks.strip(),
                          deps=list(dict.fromkeys(deps)), dep_raw=dep.strip(), blocked=blk.strip(),
                          status=st.strip(), date=date.strip()))

ids = {s["id"] for s in steps}
for s in steps:
    s["deps"] = [d for d in s["deps"] if d in ids and d != s["id"]]
    if s["id"] == "7.2":
        s["dep_raw"] = "Contratos de F1–F6 (ver STATUS.md)"

# orden cronológico: topológico, estable respecto al orden de STATUS.md
order, done = [], set()
remaining = list(steps)
while remaining:
    for s in remaining:
        if all(d in done for d in s["deps"]):
            order.append(s); done.add(s["id"]); remaining.remove(s); break
    else:
        order += remaining; break
steps = order

def done_units(s):
    n = len(s["tracks"])
    if s["status"] in ("Aprobado", "En revisión"): return n
    if s["status"] == "Gate C": return 1
    return 0
def mark(s):
    return {"Aprobado": "[x]", "En curso": "[o]", "Gate C": "[o]", "En revisión": "[o]"}.get(s["status"], "[ ]")
for s in steps:
    s["units"] = len(s["tracks"]); s["done"] = done_units(s)
    s["pct"] = round(100 * s["done"] / s["units"])
    s["mark"] = mark(s)
    s["desc"] = DESC.get(s["id"], "")
    s["note"] = NOTES.get(s["id"], "")
    if s["blocked"] not in ("—", ""):
        s["note"] = (s["note"] + " " if s["note"] else "") + f"Bloqueado por: {s['blocked']}."

# ---------- métricas ----------
tot_u = sum(s["units"] for s in steps); done_u = sum(s["done"] for s in steps)
n = len(steps); n_ok = sum(s["status"] == "Aprobado" for s in steps)
n_wip = sum(s["mark"] == "[o]" for s in steps)
pct_effort = round(100 * done_u / tot_u, 1); pct_tasks = round(100 * n_ok / n, 1)
by_status = {}
for s in steps: by_status[s["status"]] = by_status.get(s["status"], 0) + 1
tr_tot, tr_done = {}, {}
for s in steps:
    for t in s["tracks"]:
        tr_tot[t] = tr_tot.get(t, 0) + 1
        if s["status"] in ("Aprobado", "En revisión") or (s["status"] == "Gate C" and t == "C"):
            tr_done[t] = tr_done.get(t, 0) + 1
ph_stats = []
for p in phases:
    ps = [s for s in steps if s["phase"] == p["id"]]
    u = sum(s["units"] for s in ps); d = sum(s["done"] for s in ps)
    ph_stats.append(dict(id=p["id"], name=p["name"], n=len(ps), ok=sum(s["status"] == "Aprobado" for s in ps),
                         units=u, done=d, pct=round(100 * d / u)))
blocked_steps = [s for s in steps if s["blocked"] not in ("—", "")]
def section(title):
    """Body of the '## <title>…' section of STATUS.md, up to the next '## ' heading."""
    return STATUS.split("\n## " + title, 1)[1].split("\n## ", 1)[0]

def table_rows(text, header):
    rows = []
    for line in text.splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if cells[0] in (header, "---") or set(cells[0]) <= {"-"}:
            continue
        rows.append(cells)
    return rows

milestones = [tuple((r + ["", "", ""])[:3]) for r in table_rows(section("Hitos"), "Hito")]
derived = [tuple((r + ["", "", "", ""])[:4]) for r in table_rows(section("Trabajo derivado"), "Trabajo")] \
    if "\n## Trabajo derivado" in STATUS else []
questions = []
for line in STATUS.split("## Preguntas abiertas")[1].splitlines():
    m = re.match(r"\| (.+?) \| (.+?) \| (.+?) \|$", line)
    if m and m.group(1) not in ("Pregunta", "---"):
        questions.append((m.group(1), m.group(2), m.group(3)))

# ---------- TAREAS.md ----------
def esc(t): return t.replace("|", "\\|")
md = ["# TAREAS — Plan de desarrollo NEXUS", "",
      "> Generado desde `docs/steps/STATUS.md` con `python3 tools/gen_tareas.py` (CLAUDE.md §10). "
      "Panel visual: `TAREAS.html`.", "",
      "**Status:** `[ ]` no iniciada · `[o]` en marcha (incluye Gate C / En revisión: entregada pero sin aprobar) · `[x]` finalizada (`Aprobado` por Luciano).", "",
      f"**Resumen:** {n_ok}/{n} tareas aprobadas ({pct_tasks}%) · esfuerzo de codificación {done_u}/{tot_u} tracks ({pct_effort}%) · {n_wip} en marcha.", ""]
for p in phases:
    md += [f"## {p['id']} · {p['name']}", "",
           "| Status | Nombre de la tarea | Descripción | Dependencias | Comentarios |", "|---|---|---|---|---|"]
    for s in [s for s in steps if s["phase"] == p["id"]]:
        deps = ", ".join(s["deps"]) if s["deps"] else "—"
        tr = " ".join(s["tracks"])
        md.append(f"| {s['mark']} | **{s['id']}** {esc(s['title'])} (tracks {tr}; estado: {s['status']}) | {esc(s['desc'])} | {deps} | {esc(s['note'])} |")
    md.append("")
if derived:
    md += ["## Trabajo derivado de decisiones", "", "| Trabajo | Decisión | Tracks | Antes de |", "|---|---|---|---|"]
    md += [f"| {esc(a)} | {esc(b)} | {c} | {esc(d)} |" for a, b, c, d in derived]
    md += [""]
md += ["## Hitos", "", "| Hito | Condición | Confirmado |", "|---|---|---|"]
md += [f"| {a} | {b} | {c or '—'} |" for a, b, c in milestones]
(ROOT / "TAREAS.md").write_text("\n".join(md) + "\n", encoding="utf-8")

# ---------- TAREAS.html ----------
data = dict(steps=steps, phases=ph_stats, phase_names={p["id"]: p["name"] for p in phases},
            tot_u=tot_u, done_u=done_u, n=n, n_ok=n_ok, n_wip=n_wip, pct_effort=pct_effort, pct_tasks=pct_tasks,
            by_status=by_status, tr_tot=tr_tot, tr_done=tr_done, milestones=milestones, questions=questions,
            blocked=[s["id"] for s in blocked_steps])
TEMPLATE = (Path(__file__).parent / "tareas_template.html").read_text(encoding="utf-8")
(ROOT / "TAREAS.html").write_text(TEMPLATE.replace("/*DATA*/null", json.dumps(data, ensure_ascii=False)), encoding="utf-8")
print(f"OK: {n} tareas, {pct_tasks}% tareas, {pct_effort}% esfuerzo")
