import { useId, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { fetchUsers, userLabel, type UserListItem } from '../api';
import { Input, Select } from '../../../shared/components/ui/Field';
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
  const [search, setSearch] = useState('');
  const term = useDebounce(search.trim(), 300);
  const selectId = useId();

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

  return (
    <div className="flex flex-col gap-3">
      <Input
        label="Find a user"
        type="search"
        placeholder="Name or email"
        value={search}
        onChange={(event) => setSearch(event.target.value)}
      />
      <Select
        label={label}
        required
        value={value}
        onChange={(event) => choose(event.target.value)}
        error={query.isError ? 'Users could not be loaded.' : undefined}
        hint={
          query.data !== undefined && query.data.totalCount > PICKER_PAGE_SIZE
            ? `Showing the first ${PICKER_PAGE_SIZE} matches. Search to narrow the list.`
            : undefined
        }
      >
        {choices}
      </Select>
    </div>
  );
};
