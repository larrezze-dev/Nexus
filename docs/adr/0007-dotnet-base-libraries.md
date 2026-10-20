# ADR-0007 · Librerías base .NET sin licencias comerciales

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** `conventions-backend.md §10`, 0.1

## Contexto
Varias librerías habituales en .NET pasaron a licencia comercial para versiones nuevas en 2025: MediatR, AutoMapper, FluentAssertions y MassTransit. Un agente tiende a añadirlas por costumbre.

## Decisión
- Lista cerrada de paquetes en `conventions-backend.md §10`.
- Sustitutos: dispatcher propio (en vez de MediatR), mapeo manual (en vez de AutoMapper), Shouldly (en vez de FluentAssertions), Confluent.Kafka directo (en vez de MassTransit).
- Gestión central de versiones (`Directory.Packages.props`). Un paquete nuevo exige ADR.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Comprar licencias | Coste y dependencia por funcionalidad trivial de reimplementar |
| Fijar versiones antiguas gratuitas | Sin parches de seguridad a medio plazo |

## Consecuencias
- Algo más de código propio en `BuildingBlocks` (dispatcher, pipeline de validación).

## Cómo se verifica
Job de CI que falla si `Directory.Packages.props` contiene un paquete fuera de la lista.
