import type { NexusEnvironment } from './environment.model';

/**
 * Local/dev environment: Keycloak in `infra/compose`, realm `nexus`,
 * port 8180 (ADR-0006). Same code path as production Entra ID — only
 * these values differ.
 */
export const environment: NexusEnvironment = {
  production: false,
  auth: {
    authority: 'http://localhost:8180/realms/nexus',
    clientId: 'nexus-web',
    // ⚑ DUDA SIN RESOLVER (dejar que F5/el orquestador la cierren):
    // ADR-0006 dice que el *protocol mapper* de Keycloak hace que el
    // comportamiento sea equivalente al de Entra ID, pero no dice si el
    // *nombre* del scope en el realm `nexus` es literalmente
    // "api://nexus/access_as_user" (formato App ID URI de Entra ID) o un
    // client scope de Keycloak con un nombre plano (p. ej. "access_as_user"),
    // ya que Keycloak no usa la convención App ID URI de Entra ID para sus
    // scopes. Se deja aquí el mismo literal que cita la ficha 0.5 ("scope =
    // openid profile api://nexus/access_as_user o el equivalente de
    // Keycloak") para no inventar un nombre distinto sin dejar constancia:
    // quien configure el realm Keycloak (paso 0.2/infra) debe confirmar el
    // nombre real del client scope y ajustar este valor si difiere.
    scope: 'openid profile api://nexus/access_as_user',
    redirectUrl: 'http://localhost:4200',
    postLogoutRedirectUri: 'http://localhost:4200',
  },
};
