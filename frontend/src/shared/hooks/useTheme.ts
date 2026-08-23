import { useCallback, useEffect, useState } from 'react';

export type Theme = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'shophub.theme.v1';

const readStored = (): Theme => {
  const stored = localStorage.getItem(STORAGE_KEY);

  return stored === 'light' || stored === 'dark' ? stored : 'system';
};

/**
 * Dark mode with a manual toggle persisted in localStorage (spec §11.4).
 *
 * Three states, not two: 'system' removes the attribute entirely so the CSS
 * prefers-color-scheme query decides. An explicit choice stamps data-theme, which the
 * stylesheet gives precedence in both directions.
 */
export const useTheme = () => {
  const [theme, setTheme] = useState<Theme>(readStored);

  useEffect(() => {
    const root = document.documentElement;

    if (theme === 'system') {
      root.removeAttribute('data-theme');
      localStorage.removeItem(STORAGE_KEY);
    } else {
      root.setAttribute('data-theme', theme);
      localStorage.setItem(STORAGE_KEY, theme);
    }
  }, [theme]);

  const cycle = useCallback(() => {
    setTheme((current) => (current === 'light' ? 'dark' : current === 'dark' ? 'system' : 'light'));
  }, []);

  return { theme, setTheme, cycle };
};
