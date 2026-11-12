/**
 * Country names from the browser's CLDR data (`Intl.DisplayNames`), so no country list has to be
 * kept in the app or translated by hand. Codes are ISO 3166-1 alpha-2 (`countryCode` in the API).
 */

/** Region codes `Intl` knows that are not countries (EU, UN, pseudo-locales, unknown region). */
const NOT_COUNTRIES = new Set(['EU', 'EZ', 'UN', 'QO', 'XA', 'XB', 'ZZ']);

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

const namesCache = new Map<string, Intl.DisplayNames | null>();
const optionsCache = new Map<string, readonly CountryOption[]>();

/** One entry of a country select: the ISO code and its name in the UI language. */
export interface CountryOption {
  readonly code: string;
  readonly name: string;
}

function displayNames(lang: string): Intl.DisplayNames | null {
  if (!namesCache.has(lang)) {
    try {
      namesCache.set(lang, new Intl.DisplayNames([lang], { type: 'region', fallback: 'none' }));
    } catch {
      namesCache.set(lang, null);
    }
  }
  return namesCache.get(lang) ?? null;
}

/** Name of the country `code` in `lang` («CH» → «Schweiz» / «Suiza»); the code itself if unknown. */
export function countryName(code: string | null | undefined, lang: string): string {
  if (!code) {
    return '';
  }
  const upper = code.toUpperCase();
  try {
    return displayNames(lang)?.of(upper) ?? upper;
  } catch {
    return upper;
  }
}

/** All countries, sorted by name in `lang` (for a select). */
export function countryOptions(lang: string): readonly CountryOption[] {
  const cached = optionsCache.get(lang);
  if (cached) {
    return cached;
  }
  const names = displayNames(lang);
  const options: CountryOption[] = [];
  if (names) {
    for (const first of LETTERS) {
      for (const second of LETTERS) {
        const code = first + second;
        if (NOT_COUNTRIES.has(code)) {
          continue;
        }
        let name: string | undefined;
        try {
          name = names.of(code);
        } catch {
          name = undefined;
        }
        if (name && name !== code) {
          options.push({ code, name });
        }
      }
    }
  }
  const collator = new Intl.Collator([lang]);
  options.sort((a, b) => collator.compare(a.name, b.name));
  optionsCache.set(lang, options);
  return options;
}
