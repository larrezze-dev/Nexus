import { I18NText } from '../../../api/platform/models/i-18-n-text';

/**
 * Resolves the label to show for an `I18nText` caption in the active UI language.
 *
 * Fallback chain, mirroring what the contract documents for `I18nText`
 * (`org.md §3`, `conventions-frontend.md §4`: "de" is the mandatory reference language):
 * active language -> `de` -> `en` -> first available translation -> caller-supplied fallback
 * (normally the entity's `code`, which always exists even for nodes without a `captionI18n`,
 * e.g. `Group`/`LegalEntity` in the org tree).
 */
export function resolveCaption(
  caption: I18NText | null | undefined,
  language: string,
  fallback: string,
): string {
  if (!caption) {
    return fallback;
  }
  const byActiveLanguage = readLanguage(caption, language);
  if (byActiveLanguage) {
    return byActiveLanguage;
  }
  if (caption.de) {
    return caption.de;
  }
  if (caption.en) {
    return caption.en;
  }
  const firstAvailable = Object.values(caption).find((value): value is string => !!value);
  return firstAvailable ?? fallback;
}

/** `I18NText` (generated model) has no index signature; read it as a generic record instead
 * of adding per-language `if` branches for every supported language. */
function readLanguage(caption: I18NText, language: string): string | undefined {
  return (caption as unknown as Record<string, string | undefined>)[language];
}
