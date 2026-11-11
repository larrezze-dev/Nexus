# ADR-0006 · Autenticación con Microsoft Entra ID

- **Estado:** Aceptado
- **Fecha:** 2026-10-18
- **Afecta a:** 0.5, 0.4 (`app_user`), 6.4 (móvil)

## Contexto
Los manuales definen `User` y RBAC, pero no cómo se autentica nadie. El despliegue es Azure (D10).

## Decisión
- Proveedor de identidad: **Microsoft Entra ID**, OIDC + OAuth 2.0.
- Registro de aplicaciones: una API (`api://nexus`) con un scope `access_as_user`; un cliente SPA (web); un cliente público móvil.
- Web: flujo Authorization Code + PKCE con `angular-auth-oidc-client` (cliente OIDC certificado, MIT). Móvil: AppAuth-Android. **No se usa MSAL**: MSAL solo habla con la plataforma de identidad de Microsoft y obligaría a tener dos caminos de código (Entra ID en Azure, IdP de desarrollo en local). Con librerías OIDC genéricas el código es el mismo y solo cambia la configuración (`authority`, `clientId`, `scope`).
- Backend: `Microsoft.AspNetCore.Authentication.JwtBearer` estándar (valida emisor, audiencia y firma con el `jwks_uri` del emisor configurado). Igual con Entra ID y con el IdP local.
- El gateway valida el JWT (emisor, audiencia, firma) y reenvía el token a los servicios, que lo vuelven a validar.
- Correspondencia token → usuario: claim `oid` → `org.app_user.external_subject`. Un `oid` sin `app_user` activo recibe 403 `user_not_provisioned`.
- **Vinculación en el primer login `[+]` (decidido 2026-11-11):** un `oid` sin `app_user` puede vincularse a un `app_user` **activo y sin `external_subject`** cuyo `email` coincida, sin distinguir mayúsculas, con la identidad del token. Condiciones y mecánica:
  - **Solo cuentas miembro del tenant:** `tid` igual al tenant configurado, cuenta no invitada (claim opcional `acct = 0`) y dominio del UPN (`preferred_username`) dentro de `Nexus:Identity:LinkableDomains` (dominios verificados de Nüssli). Nunca se usa el claim `email` de Entra, que el usuario puede cambiar. En el IdP local: `email` con `email_verified = true`.
  - **Invitados B2B:** se dan de alta con su `oid` a mano, como hoy.
  - **Solo `nexus-platform` vincula** (ARC-R1: es el único que escribe `org.app_user`). Lo hace en su resolución de identidad, antes de responder 403, y la SPA llama a `GET /v1/me` al arrancar. El resto de servicios responden 403 `user_not_provisioned` hasta que el usuario pasa por platform.
  - **Escritura sin carrera:** `UPDATE org.app_user SET external_subject = $oid WHERE id = $id AND external_subject IS NULL`. Actor del evento `nexus.org.user.updated`: el usuario SYSTEM (todavía no hay `UserId`).
  - Si no hay ninguno, o hay más de uno → 403 `user_not_provisioned`. **Nunca se crean usuarios** en el login, y los roles siguen siendo manuales (A6).
  - Sirve para los usuarios que crea la migración sin `oid` (`migration.md`, fila 4; 7.2/7.3 cargan el email conciliado con el UPN de Entra). La API de alta sigue exigiendo `oid` (`org.md §3`).
- **Los roles de Entra ID no se usan para autorizar.** La autorización es solo RBAC de NEXUS (A6).
- Freelancers sin cuenta corporativa: cuentas invitadas (B2B) en el mismo tenant ⚑ confirmar con IT.
- Local y CI: Keycloak en modo dev (`infra/compose`, puerto 8180) con el realm `nexus` y los usuarios de `OrgFixture`. Un *protocol mapper* emite el claim `oid` con el mismo UUID que `org.app_user.external_subject` de la semilla dev, para que la correspondencia token → usuario sea idéntica a la de Entra ID.

## Alternativas descartadas
| Alternativa | Por qué no |
|---|---|
| Identidad propia (ASP.NET Identity) | Gestionar contraseñas, MFA y bajas; nada de eso es negocio |
| Roles de Entra como autorización | Sin alcance por Mandant; duplica A6 |
| MSAL Angular / MSAL Android | Solo funciona con Microsoft Entra; impide usar el IdP local con el mismo código. Se pierde el SSO del broker de Android (aceptable) |

## Consecuencias
- Altas y bajas de empleados siguen el ciclo de IT.
- Hay que crear y mantener `app_user`: alta manual en 0.4 con `oid`, y vinculación por identidad del tenant en el primer login para los usuarios sin `oid` (sin alta automática; decidido 2026-11-11).

## Cómo se verifica
Test e2e de 0.5: login en local con el IdP de desarrollo y `GET /api/platform/v1/me` → 200. Token sin `app_user` → 403.
