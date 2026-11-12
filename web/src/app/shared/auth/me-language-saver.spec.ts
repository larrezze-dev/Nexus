import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { Api } from '../../api/platform/api';
import { updateMe } from '../../api/platform/fn/me/update-me';
import { NX_LANGUAGE_PREFERENCE_SAVER } from '../../core/i18n/nx-i18n.service';
import { provideMeLanguageSaver } from './me-language-saver';

describe('provideMeLanguageSaver', () => {
  function setup(invoke: () => Promise<unknown>) {
    const api = { invoke: vi.fn(invoke) };
    TestBed.configureTestingModule({
      providers: [{ provide: Api, useValue: api }, provideMeLanguageSaver()],
    });
    return { saver: TestBed.inject(NX_LANGUAGE_PREFERENCE_SAVER), api };
  }

  it('saves the picked language as the preference with PATCH /v1/me', async () => {
    const { saver, api } = setup(async () => ({ preferredLanguage: 'es' }));
    await saver('es');
    expect(api.invoke).toHaveBeenCalledWith(updateMe, { body: { preferredLanguage: 'es' } });
  });

  it('propagates a failure so NxI18nService can ignore it', async () => {
    const { saver } = setup(() => Promise.reject(new Error('503')));
    await expect(saver('fr')).rejects.toThrow('503');
  });
});
