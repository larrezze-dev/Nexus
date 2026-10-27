/**
 * Default/production environment.
 *
 * Replaced by `environment.development.ts` for the `development` build
 * configuration (see `fileReplacements` in `angular.json`), which is what
 * `ng serve` (default configuration) and `ng test` use.
 *
 * ADR-0006: web authentication is Authorization Code + PKCE via
 * `angular-auth-oidc-client`, same code against Entra ID (Azure) and
 * Keycloak (local) — only these values change.
 *
 * The `NexusEnvironment` type lives in `environment.model.ts`, not here —
 * see that file for why.
 */
import type { NexusEnvironment } from './environment.model';

export const environment: NexusEnvironment = {
  production: true,
  auth: {
    // Placeholder: the real Entra ID tenant for Azure is not provisioned by
    // this step (0.5 track F only covers the web app). Infra/IAM must fill
    // this in (and the corresponding build-time replacement) once the Entra
    // ID app registration from ADR-0006 exists.
    authority: 'https://login.microsoftonline.com/<tenant-id>/v2.0',
    clientId: 'nexus-web',
    scope: 'openid profile api://nexus/access_as_user',
    redirectUrl: 'https://TBD.nexus.example.com',
    postLogoutRedirectUri: 'https://TBD.nexus.example.com',
  },
};
