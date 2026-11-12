import { countryName, countryOptions } from './country-names';

describe('country names', () => {
  it('names a country in the UI language and falls back to the code', () => {
    expect(countryName('CH', 'de')).toBe('Schweiz');
    expect(countryName('es', 'es')).toBe('España');
    expect(countryName('', 'es')).toBe('');
    expect(countryName(null, 'es')).toBe('');
  });

  it('lists real countries sorted by name, without EU/UN or pseudo regions', () => {
    const options = countryOptions('es');
    const codes = options.map((option) => option.code);
    expect(codes).toContain('CH');
    expect(codes).toContain('ES');
    expect(codes).not.toContain('EU');
    expect(codes).not.toContain('UN');
    expect(codes).not.toContain('ZZ');
    const names = options.map((option) => option.name);
    expect([...names].sort(new Intl.Collator(['es']).compare)).toEqual(names);
  });
});
