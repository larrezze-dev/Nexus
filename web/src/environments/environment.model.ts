/**
 * Shared shape for `environment.ts` / `environment.development.ts`.
 *
 * Lives in its own file (not inside `environment.ts`) on purpose:
 * `angular.json`'s `fileReplacements` swaps the *path* `./environment` for
 * `./environment.development` as a whole for the `development`
 * configuration, so anything importing the type FROM `./environment`
 * (including `environment.development.ts` itself) would otherwise resolve
 * back to whichever of the two files is active, and when it's
 * `environment.development.ts`, that becomes a self-import with no
 * `NexusEnvironment` export — which is exactly the error this split avoids.
 */
export interface NexusEnvironment {
  readonly production: boolean;
  readonly auth: {
    /** OIDC issuer. Entra ID tenant in Azure, Keycloak realm in local/dev. */
    readonly authority: string;
    /** SPA client registered at the IdP. Same literal in every environment. */
    readonly clientId: string;
    /** Space-separated OIDC scopes requested from the IdP. */
    readonly scope: string;
    readonly redirectUrl: string;
    readonly postLogoutRedirectUri: string;
  };
}
