# Entorno local (Docker Compose)

Infraestructura de desarrollo de NEXUS levantada con Docker Compose. Todo el entorno local es solo para desarrollo: los datos son descartables y las credenciales de este documento no son válidas fuera de local.

## Servicios

| Servicio | Imagen | Puerto(s) | URL local | Notas |
|---|---|---|---|---|
| PostgreSQL | ver `docker-compose.yml` | 5432 | `postgresql://localhost:5432/nexus` | BD `nexus`; roles y esquemas llegan en 0.3 |
| Kafka (KRaft) | ver `docker-compose.yml` | 9092 | `localhost:9092` | `auto.create.topics.enable=false` |
| Schema Registry | ver `docker-compose.yml` | 8081 | `http://localhost:8081` | compatibilidad `BACKWARD` |
| Kafka UI | ver `docker-compose.yml` | 8085 | `http://localhost:8085` | explorar topics, consumer groups y el Schema Registry |
| ClickHouse | ver `docker-compose.yml` | 8123 (HTTP) / 9000 (nativo) | `http://localhost:8123` | BD `nexus_analytics` |
| Azurite | ver `docker-compose.yml` | 10000 | `http://localhost:10000/devstoreaccount1` | solo Blob Storage |
| Keycloak | ver `docker-compose.yml` | 8180 | `http://localhost:8180` | realm `nexus`, consola admin en `/admin` |
| Aspire Dashboard (OTLP) | ver `docker-compose.yml` | 4317 (OTLP gRPC) / 18888 (UI) | `http://localhost:18888` | trazas, métricas y logs OpenTelemetry |

> Las imágenes y versiones exactas están fijadas en `infra/compose/docker-compose.yml`; esta tabla no las duplica para evitar que queden desfasadas.

## Credenciales de desarrollo (Keycloak)

Usuarios del realm `nexus` para probar los distintos alcances de acceso. **Nunca válidas fuera de local.**

| Usuario | Contraseña | Alcance |
|---|---|---|
| `admin` | `admin` | GLOBAL |
| `usera` | `usera` | Legal Entity CH |
| `userb` | `userb` | Legal Entity DE |
| `wha` | `wha` | Solo almacén de LE-CH |
| `unprovisioned` | `unprovisioned` | Sin `app_user` provisionado (debe dar 403) |

## Cómo arrancar

```bash
make up                  # levanta todo el entorno (perfil `infra`)
make doctor              # verifica la salud de los servicios
make logs S=<servicio>   # sigue los logs de un servicio (p. ej. S=kafka)
make down                # para los contenedores, conserva los volúmenes
make reset               # para y borra volúmenes: pierde los datos locales (intencional y seguro en desarrollo)
```

## Notas

- El contenido real de PostgreSQL (roles y esquemas) llega en el paso 0.3.
- Los contratos de servicio (`contracts/openapi/<servicio>.yaml`) empiezan a mockearse con Prism (perfil `mock`) desde el paso en que nace cada servicio.
