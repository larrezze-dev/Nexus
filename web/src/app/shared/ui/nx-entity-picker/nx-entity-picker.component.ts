import { ChangeDetectionStrategy, Component, EventEmitter, Output, effect, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import type { ErrorStateMatcher } from '@angular/material/core';
import { MatIconModule } from '@angular/material/icon';
import { MatAutocompleteModule, type MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule } from '@jsverse/transloco';
import { Observable, Subject, catchError, debounce, distinctUntilChanged, of, switchMap, tap, timer } from 'rxjs';

/**
 * Generic remote-search entity picker, e.g. "pick a `LegalEntity` when
 * creating a `BusinessUnit`" or "pick a `BusinessUnit` when creating a
 * `Warehouse`". Knows nothing about any concrete entity: the caller's
 * facade supplies `search` (how to fetch matching rows for a query) and
 * `displayWith` (how to render one row as text); this component only owns
 * the debounce, the in-flight/loading state and the autocomplete UI.
 *
 * Not a `ControlValueAccessor`: unlike `nx-i18n-caption-editor`, the picker
 * doesn't carry an atomic "value" a parent form reads back symmetrically —
 * selecting an entity is a one-shot event (`(selected)`), typically used to
 * populate a *different* control (e.g. `legalEntityId`) with the picked
 * item's id. Callers that need it inside a Reactive Form bind that other
 * control from the `(selected)` handler.
 *
 * As a form field (`nx-ui-page-templates`, entity selector): `label` puts the label inside the
 * field, `value` shows the chosen item («code — name») in it, `clearable` adds a «×» that emits
 * `(cleared)`, and `hint`/`error` go under it like any `mat-form-field`. Typing searches again;
 * closing the list without picking puts the chosen item (or nothing) back.
 */
@Component({
  selector: 'nx-entity-picker',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoModule,
  ],
  templateUrl: './nx-entity-picker.component.html',
  styleUrl: './nx-entity-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NxEntityPickerComponent<T> {
  /** Remote search function, e.g. `(q) => legalEntityFacade.search(q)`. Called lazily, never at setup time. */
  readonly search = input.required<(query: string) => Observable<T[]>>();

  /** How to render one result/selected item as text, e.g. `(le) => le.code`. */
  readonly displayWith = input.required<(item: T) => string>();

  readonly placeholderKey = input<string>('shared.entityPicker.placeholder');

  /** Debounce before a search fires after the last keystroke. */
  readonly debounceMs = input<number>(300);

  readonly disabled = input<boolean>(false);

  /** Label inside the field (already translated); none = a bare search box. */
  readonly label = input<string | null>(null);

  /** Muted help under the field (already translated). */
  readonly hint = input<string | null>(null);

  /** Error under the field (already translated); while set, the field shows its error state. */
  readonly error = input<string | null>(null);

  /**
   * Text (already translated) shown instead of the muted help when the search fails with 403, so a user
   * who may not list the entity sees why the list is empty rather than a mute «No results».
   */
  readonly deniedMessage = input<string | null>(null);

  /** The last search was refused (403). */
  protected readonly denied = signal(false);

  /** Marks the label as required (asterisk). Validation stays with the caller's form. */
  readonly required = input<boolean>(false);

  /** The chosen item, shown as text in the field; `null` = nothing chosen. */
  readonly value = input<T | null>(null);

  /** Shows a «×» button while an item is chosen; it emits `(cleared)`. */
  readonly clearable = input<boolean>(false);

  /** Accessible name of the «×» button (already translated). */
  readonly clearLabel = input<string | null>(null);

  /** The «×» was pressed: the caller empties its control. */
  @Output() readonly cleared = new EventEmitter<void>();

  /** The field shows its error state exactly while `error` is set. */
  protected readonly errorState: ErrorStateMatcher = { isErrorState: () => !!this.error() };

  /** Emits the selected item (not just its id) so the caller's facade decides what to keep. */
  @Output() readonly selected = new EventEmitter<T>();

  protected readonly control = new FormControl<string | T>('', { nonNullable: true });
  protected readonly loading = signal(false);
  protected readonly options = signal<readonly T[]>([]);

  private readonly query$ = new Subject<string>();

  constructor() {
    this.query$
      .pipe(
        // `debounce(() => timer(...))`, not `debounceTime(this.debounceMs())`: the latter would
        // read the `debounceMs` signal input once, while building the pipe in the constructor —
        // before Angular has assigned any caller-provided value to it — permanently locking in
        // the input's *default*. `debounce`'s duration selector runs lazily, per emission, well
        // after inputs are set, so a later `[debounceMs]` override is honored correctly.
        debounce(() => timer(this.debounceMs())),
        distinctUntilChanged(),
        tap(() => this.loading.set(true)),
        switchMap((query) => {
          this.denied.set(false);
          return this.search()(query).pipe(
            catchError((error: unknown) => {
              this.denied.set((error as { status?: number } | null)?.status === 403);
              return of<T[]>([]);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((results) => {
        this.options.set(results);
        this.loading.set(false);
      });

    this.control.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      if (typeof value === 'string') {
        this.query$.next(value);
      }
    });

    // The chosen item from the caller is what the field shows.
    effect(() => {
      const value = this.value();
      untracked(() => this.control.setValue(value ?? '', { emitEvent: false }));
    });

    effect(() => {
      if (this.disabled()) {
        this.control.disable({ emitEvent: false });
      } else {
        this.control.enable({ emitEvent: false });
      }
    });
  }

  /** Bound to `[displayWith]` on `mat-autocomplete`; also used in the template for each option's label. */
  protected displayFn = (value: string | T): string => {
    if (typeof value === 'string') {
      return value;
    }
    return this.displayWith()(value);
  };

  protected onOptionSelected(event: MatAutocompleteSelectedEvent): void {
    this.selected.emit(event.option.value as T);
  }

  /** Focusing an empty field lists the first matches right away (no typing needed for short lists). */
  protected onFocus(): void {
    if (this.options().length === 0 && !this.loading()) {
      const value = this.control.value;
      this.query$.next(typeof value === 'string' ? value : '');
    }
  }

  /** Closing the list with only typed text puts the chosen item (or nothing) back in the field. */
  protected onClosed(): void {
    if (typeof this.control.value === 'string' && this.label() !== null) {
      this.control.setValue(this.value() ?? '', { emitEvent: false });
    }
  }

  protected clear(event: Event): void {
    event.stopPropagation();
    this.control.setValue('', { emitEvent: false });
    this.cleared.emit();
  }
}
