---
name: nx-ui-visual-review
description: Capturas reales de NEXUS web (de+es, 1440 y 390 px, página completa, todas las pestañas) y crítica de pantalla completa con checklist. Úsala antes de dar por terminada cualquier pantalla o componente visible, y al revisar trabajo de UI.
---

# Revisión visual de NEXUS web

Una pantalla no está terminada hasta que la has **visto** en alemán y en español, entera, a 1440 y a 390 px. Los tests en verde no dicen nada del aspecto. Origen: en 0.8-F las previews se tomaron en `es`, sin Roboto y solo del componente nuevo, y Luciano encontró la app «un desastre» (memoria `feedback_ui_quality`).

## 1. Levantar la app

- Backend contra el entorno nexus-* de Luciano (Postgres.app ocupa `localhost:5432`, usa la IP LAN):
  ```bash
  set -a; source <repo>/infra/compose/.env; set +a
  IP=$(ipconfig getifaddr en0)
  export Nexus__Database__ConnectionString="Host=$IP;Port=5432;Database=nexus;Username=svc_platform"
  export Nexus__Database__Password="$NEXUS_SVC_PLATFORM_PASSWORD" Nexus__ClickHouse__Password="$CLICKHOUSE_PASSWORD"
  # en el worktree: dotnet build + dotnet run --no-build de src/Platform/Nexus.Platform.Api (:5101) y src/Gateway/Nexus.Gateway (:8080)
  ```
  En zsh, ejecuta los comandos guardados en variables con `bash -c`.
- Web: el cliente API generado está en `.gitignore`. En un worktree nuevo, genéralo antes de `ng serve` (`node tools/bundle-openapi.cjs contracts/openapi/platform.yaml <tmp>/platform.json && web/node_modules/.bin/ng-openapi-gen -i <tmp>/platform.json -o web/src/app/api/platform`), o `ng serve` no arranca.
- Si `ng serve` ya está corriendo de otro worktree en :4200, **no** lo mates sin avisar al orquestador.
- Usuarios Keycloak: admin/admin (idioma `de`), usera, userb, wha.

## 2. Capturar

```bash
cd <scratch>/pw && npm i playwright@1.65.0      # una vez; nunca en web/package.json
NX_PW_DIR=<scratch>/pw node .claude/skills/nx-ui-visual-review/scripts/shots.mjs \
  --out <scratch>/shots/<tarea> --langs de,es --widths 1440,390 --tabs \
  --routes .claude/skills/nx-ui-visual-review/scripts/routes.txt   # o --route /x --route /y
```
- El script fuerza el idioma interceptando `/me` (no toca la BD), mide el scroll de `mat-sidenav-content` para la página completa, guarda también la primera carga (`-firstload.png`, para detectar claves crudas) y escribe `report.json` (texto, claves crudas, fuentes calculadas, `html lang`, errores de consola, overflow horizontal).
- Usa las fuentes del sistema (sin Roboto instalado en el Mac): así se ve lo mismo que en el Chrome de Luciano. No instales fuentes en la máquina para que «salga bien».
- Si hay extensión de Chrome conectada, una captura en el navegador real vale más que la headless.

## 3. Mirar (obligatorio)

Abre con `Read` **cada** PNG que cites. Si no lo has abierto, no lo describas.
Revisa también `report.json`: `rawKeys` vacío, `consoleErrors` vacío, `horizontalOverflow=false`, y las fuentes sin serif.

## 4. Checklist de pantalla completa

Se critica la pantalla entera (shell, migas, cabecera, contenido, pie), no solo lo que has tocado:

- [ ] **Idioma**: todo en el idioma activo; nada en inglés o alemán colado (paginador, «Loading…», fechas, enums, captions de datos).
- [ ] **Claves crudas**: ninguna, tampoco en la primera carga.
- [ ] **Textos técnicos**: ni UUIDs, ni `true/false`, ni códigos de permiso, ni «paso 2.1», ni nombres de campo del API. Lenguaje de usuario final.
- [ ] **Tipografía**: una sola familia, sin serif; escala de títulos coherente con la plantilla.
- [ ] **Alineación**: bordes izquierdos de título, migas, filtros, grid y formulario sobre la misma línea; controles de una fila con la misma altura y la misma línea base.
- [ ] **Jerarquía**: un único botón primario por vista; acciones destructivas separadas y en estilo secundario; cabecera título + estado + acciones en una fila.
- [ ] **Espaciado**: solo la escala `--nx-space-*`; nada pegado (p. ej. «Global2026-11-07»).
- [ ] **Plantilla**: la página sigue la plantilla de lista, detalle o formulario de `nx-ui-page-templates`, sin variaciones locales.
- [ ] **Migas**: presentes, sin duplicados, último nivel = nombre del registro.
- [ ] **Estados**: cargando, vacío, error y sin permiso diseñados y vistos (fuerza el estado si hace falta: lista vacía con filtro, 403/404 con un id inventado).
- [ ] **Grid**: muestra todas las filas de la página sin cortes; cabeceras traducidas; sin columnas truncadas a 1440.
- [ ] **Móvil 390 px**: sin overflow horizontal; el menú lateral no tapa el contenido.
- [ ] **Consola**: sin errores.

## 5. Entrega

Devuelve: rutas de las capturas (de/es × 1440/390), la checklist marcada con lo que **no** cumple y por qué, y los defectos vistos fuera de tu alcance (se reportan siempre, aunque no los arregles).
