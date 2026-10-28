import { SupportedLanguage } from '../../core/i18n/supported-languages';

/**
 * Translatable caption/label used across contexts (`BusinessUnit.captionI18n`,
 * `Warehouse.captionI18n`, …). Mirrors the `I18nText` schema in
 * `contracts/openapi/_shared/components.yaml`: `de` is required (reference
 * language), the rest are filled in as translations become available.
 *
 * Kept as a hand-written shared type rather than re-exporting a generated
 * model: `I18nText` is a cross-context concept (`shared/ui`), not something
 * owned by a single service's OpenAPI contract.
 */
export type I18nText = { readonly de: string } & Partial<Record<Exclude<SupportedLanguage, 'de'>, string>>;

/** An empty `I18nText` with every supported language present and blank. */
export function emptyI18nText(): I18nText {
  return { de: '', en: '', fr: '', it: '', es: '' };
}
