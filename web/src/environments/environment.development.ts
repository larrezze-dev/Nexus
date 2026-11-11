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
    // En Keycloak el client scope se llama literalmente `access_as_user`
    // (infra/compose/keycloak/realm-nexus.json) y añade aud=api://nexus
    // mediante su audience mapper. En Entra ID (environment.ts) se mantiene
    // la forma App ID URI `api://nexus/access_as_user`.
    scope: 'openid profile access_as_user',
    redirectUrl: 'http://localhost:4200',
    postLogoutRedirectUri: 'http://localhost:4200',
  },
};
