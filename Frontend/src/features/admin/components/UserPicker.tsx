import { useId, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { fetchUsers, userLabel, type UserListItem } from '../api';
import { Input } from '../../../shared/components/ui/Field';
import { useDebounce } from '../../../shared/hooks/useDebounce';
import { cn } from '../../../shared/lib/cn';

/** The list endpoint's page size ceiling. A narrower search is how an administrator reaches the rest. */
const PICKER_PAGE_SIZE = 100;

interface UserPickerProps {
  label: string;
  /** The chosen user's id, or an empty string. */
  value: string;
  onChange: (user: UserListItem | null) => void;
  /** The empty choice, such as "All users". Left out, a user has to be chosen. */
  emptyLabel?: string;
  /**
   * Kept in the options even when the current search would filter it out, so the choice on
   * screen never silently disappears.
   */
  selected?: UserListItem | null;
  /** `filter` renders inline for a toolbar; `form` renders stacked fields for a dialog. */
  variant?: 'form' | 'filter';
}

/**
 * Chooses a user by full name and email.
 *
 * Users are listed through the paged users endpoint like every other list, so the search box
 * narrows the server query rather than filtering a full download in the browser.
 */
export const UserPicker = ({
  label,
  value,
  onChange,
  emptyLabel,
  selected = null,
  variant = 'form',
}: UserPickerProps) => {
  // `form` types into the field itself, so the box starts showing whoever is already chosen.
  // The dialog keeps this mounted between openings, so the caller remounts it with a `key`
  // when the row it was opened from changes rather than this syncing itself to a prop.
  const [search, setSearch] = useState(variant === 'form' && selected !== null ? userLabel(selected) : '');
  const selectedLabel = selected !== null ? userLabel(selected) : null;
  // Holding the chosen user's own label is not a search for them: the endpoint matches a name
  // or an email, so sending "Name (email)" would match nothing and empty the list under the
  // box the moment a choice was made.
  const term = useDebounce(search.trim() === selectedLabel ? '' : search.trim(), 300);
  const selectId = useId();
  const listId = useId();

  const query = useQuery({
    queryKey: ['admin', 'users', 'picker', term],
    queryFn: ({ signal }) => {
      const params = new URLSearchParams({ pageSize: String(PICKER_PAGE_SIZE), sort: 'firstName' });

      if (term !== '') {
        params.set('search', term);
      }

      return fetchUsers(params, signal);
    },
    placeholderData: keepPreviousData,
  });

  const users = query.data?.items ?? [];
  const options = selected !== null && !users.some((user) => user.id === selected.id) ? [selected, ...users] : users;

  const choose = (id: string) => onChange(options.find((user) => user.id === id) ?? null);

  const choices = (
    <>
      {(emptyLabel !== undefined || value === '') && <option value="">{emptyLabel ?? 'Choose a user'}</option>}
      {query.isPending && options.length === 0 && (
        <option value="" disabled>
          Loading users…
        </option>
      )}
      {options.map((user) => (
        <option key={user.id} value={user.id}>
          {userLabel(user)}
        </option>
      ))}
    </>
  );

  if (variant === 'filter') {
    return (
      <div className="flex items-center gap-2 text-sm text-content-muted">
        <label htmlFor={selectId}>{label}</label>
        <input
          type="search"
          aria-label={`Search ${label.toLowerCase()} by name or email`}
          placeholder="Name or email"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="h-9 w-36 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content"
        />
        <select
          id={selectId}
          value={value}
          onChange={(event) => choose(event.target.value)}
          className={cn(
            'h-9 max-w-64 rounded-lg border border-border-subtle bg-surface px-2 text-sm text-content',
            query.isError && 'border-danger',
          )}
        >
          {choices}
        </select>
      </div>
    );
  }

  // One field rather than a search box above a select: the box is the search, and the
  // suggestions under it are the choices. `list` makes it a combobox, which the browser
  // filters as you type on top of the server-side narrowing.
  const byLabel = (text: string) => options.find((user) => userLabel(user) === text) ?? null;

  const nothingMatches =
    value === '' && search.trim() !== '' && !query.isPending && options.length === 0;

  return (
    <>
      <Input
        label={label}
        required
        type="search"
        list={listId}
        // The browser's own saved-values dropdown would compete with the suggestions.
        autoComplete="off"
        placeholder="Search by name or email"
        value={search}
        onChange={(event) => {
          const text = event.target.value;
          const picked = byLabel(text);

          setSearch(text);
          onChange(picked);
        }}
        error={query.isError ? 'Users could not be loaded.' : undefined}
        hint={
          nothingMatches
            ? 'No user matches that name or email.'
            : query.data !== undefined && query.data.totalCount > PICKER_PAGE_SIZE
              ? `Showing the first ${PICKER_PAGE_SIZE} matches. Keep typing to narrow them.`
              : undefined
        }
      />

      <datalist id={listId}>
        {options.map((user) => (
          <option key={user.id} value={userLabel(user)} />
        ))}
      </datalist>
    </>
  );
};
