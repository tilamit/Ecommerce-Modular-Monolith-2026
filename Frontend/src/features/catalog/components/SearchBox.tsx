import { useEffect, useId, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Search } from 'lucide-react';
import { suggestProducts } from '../api';
import { useDebounce } from '../../../shared/hooks/useDebounce';
import { formatCurrency } from '../../../shared/lib/format';

/** Spec §11.4: debounce 250-300ms. */
const DEBOUNCE_MS = 250;

const MIN_TERM_LENGTH = 2;

/**
 * Typeahead search (spec §11.4).
 *
 * Keyboard-navigable (↑↓/Enter/Esc), debounced, cancels in-flight requests, highlights the
 * matched prefix and says "no results" rather than showing an empty dropdown.
 *
 * Implemented as an ARIA combobox rather than a plain input plus a div: without the
 * listbox roles and `aria-activedescendant`, a screen reader announces nothing as the user
 * arrows through the suggestions.
 */
export const SearchBox = () => {
  const navigate = useNavigate();
  const listboxId = useId();

  const [term, setTerm] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const containerRef = useRef<HTMLDivElement>(null);

  const debouncedTerm = useDebounce(term, DEBOUNCE_MS);
  const enabled = debouncedTerm.trim().length >= MIN_TERM_LENGTH;

  const { data: suggestions = [], isFetching } = useQuery({
    queryKey: ['products', 'suggest', debouncedTerm],
    // TanStack passes an AbortSignal; forwarding it is what cancels the superseded request.
    queryFn: ({ signal }) => suggestProducts(debouncedTerm.trim(), signal),
    enabled,
    staleTime: 30_000,
  });

  // Close when focus or a click leaves the combobox entirely.
  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      if (containerRef.current !== null && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    };

    document.addEventListener('pointerdown', onPointerDown);

    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, []);

  const go = (slug: string) => {
    setIsOpen(false);
    setTerm('');
    navigate(`/products/${slug}`);
  };

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') {
      setIsOpen(false);
      setActiveIndex(-1);
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      // Stop the caret jumping to the ends of the input while navigating the list.
      event.preventDefault();

      if (suggestions.length === 0) {
        return;
      }

      setIsOpen(true);
      setActiveIndex((current) => {
        const next = event.key === 'ArrowDown' ? current + 1 : current - 1;

        // Wrap, so the list is a loop rather than a dead end at either end.
        return (next + suggestions.length) % suggestions.length;
      });

      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();

      const selected = suggestions[activeIndex];

      if (selected !== undefined) {
        go(selected.slug);
      } else if (term.trim().length > 0) {
        setIsOpen(false);
        navigate(`/?search=${encodeURIComponent(term.trim())}`);
      }
    }
  };

  const showDropdown = isOpen && enabled;

  return (
    <div ref={containerRef} className="relative flex-1">
      <div className="relative">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-content-muted"
          aria-hidden="true"
        />
        <input
          type="search"
          value={term}
          onChange={(event) => {
            setTerm(event.target.value);
            setIsOpen(true);
            setActiveIndex(-1);
          }}
          onFocus={() => setIsOpen(true)}
          onKeyDown={onKeyDown}
          placeholder="Search products…"
          aria-label="Search products"
          role="combobox"
          aria-expanded={showDropdown}
          aria-controls={listboxId}
          aria-autocomplete="list"
          aria-activedescendant={
            activeIndex >= 0 && suggestions[activeIndex] !== undefined
              ? `${listboxId}-${suggestions[activeIndex].id}`
              : undefined
          }
          className="h-9 w-full rounded-lg border border-border-subtle bg-surface pl-9 pr-3 text-sm text-content placeholder:text-content-muted"
        />
      </div>

      {showDropdown && (
        <ul
          id={listboxId}
          role="listbox"
          aria-label="Product suggestions"
          className="absolute inset-x-0 top-full z-50 mt-1 max-h-80 overflow-auto rounded-lg border border-border-subtle bg-surface-raised py-1 shadow-lg"
        >
          {suggestions.length === 0 ? (
            // An empty dropdown reads as "broken"; saying so explicitly does not.
            <li className="px-3 py-6 text-center text-sm text-content-muted">
              {isFetching ? 'Searching…' : `No products match “${debouncedTerm.trim()}”`}
            </li>
          ) : (
            suggestions.map((suggestion, index) => (
              <li
                key={suggestion.id}
                id={`${listboxId}-${suggestion.id}`}
                role="option"
                aria-selected={index === activeIndex}
                onPointerDown={() => go(suggestion.slug)}
                onPointerEnter={() => setActiveIndex(index)}
                className={`flex cursor-pointer items-center gap-3 px-3 py-2 text-sm ${
                  index === activeIndex ? 'bg-surface-sunken' : ''
                }`}
              >
                {suggestion.imageUrl !== null && suggestion.imageUrl !== undefined && (
                  <img
                    src={suggestion.imageUrl}
                    alt=""
                    width={32}
                    height={32}
                    loading="lazy"
                    className="size-8 rounded object-cover"
                  />
                )}

                <span className="flex-1 truncate text-content">
                  <HighlightedPrefix text={suggestion.name} prefix={debouncedTerm.trim()} />
                </span>

                <span className="text-content-muted">
                  {formatCurrency(suggestion.price, suggestion.currencyCode)}
                </span>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
};

/**
 * Bolds the matched portion (spec §11.4).
 *
 * The backend matches on prefix, so highlighting the leading substring reflects what
 * actually matched rather than inventing a mid-word match the query never made.
 */
const HighlightedPrefix = ({ text, prefix }: { text: string; prefix: string }) => {
  if (prefix.length === 0 || !text.toLowerCase().startsWith(prefix.toLowerCase())) {
    return <>{text}</>;
  }

  return (
    <>
      <mark className="bg-transparent font-semibold text-content">{text.slice(0, prefix.length)}</mark>
      {text.slice(prefix.length)}
    </>
  );
};
