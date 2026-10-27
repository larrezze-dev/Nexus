/**
 * Languages supported by NEXUS (conventions-frontend.md §1 and §7).
 * `de` is both the default and the final fallback of the initial-language chain.
 */
export const SUPPORTED_LANGUAGES = ['de', 'en', 'fr', 'it', 'es'] as const;

export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const DEFAULT_LANGUAGE: SupportedLanguage = 'de';

export interface LanguageOption {
  readonly code: SupportedLanguage;
  readonly label: string;
}

/** Labels are the language's own native name, so they read correctly regardless of the active language. */
export const LANGUAGE_OPTIONS: readonly LanguageOption[] = [
  { code: 'de', label: 'Deutsch' },
  { code: 'en', label: 'English' },
  { code: 'fr', label: 'Français' },
  { code: 'it', label: 'Italiano' },
  { code: 'es', label: 'Español' },
];

export function isSupportedLanguage(value: string | null | undefined): value is SupportedLanguage {
  return !!value && (SUPPORTED_LANGUAGES as readonly string[]).includes(value);
}

/**
 * Normalizes a BCP-47-ish language tag (e.g. `en-US`, `DE`, `fr_CH`) to one of our
 * supported two-letter codes, or `null` if it isn't supported.
 */
export function normalizeToSupportedLanguage(value: string | null | undefined): SupportedLanguage | null {
  if (!value) {
    return null;
  }
  const code = value.slice(0, 2).toLowerCase();
  return isSupportedLanguage(code) ? code : null;
}
