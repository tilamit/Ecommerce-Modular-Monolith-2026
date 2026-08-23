import { useEffect, useState } from 'react';

/**
 * Debounces a rapidly-changing value.
 *
 * Used by the typeahead at 250ms (spec §11.4): fast enough to feel live, slow enough that
 * a normal typist does not fire a request per keystroke.
 */
export const useDebounce = <T>(value: T, delayMs: number): T => {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);

    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
};
