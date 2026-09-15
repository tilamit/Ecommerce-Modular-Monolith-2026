import { describe, expect, it } from 'vitest';
import { toSections } from './AdminLayout';
import type { MenuNode } from '../shared/api/types';

const node = (id: string, title: string, route: string | null): MenuNode => ({
  id,
  title,
  icon: null,
  route,
  displayOrder: 0,
  children: [],
});

const ADMIN = [
  node('a1', 'Dashboard', '/admin/dashboard'),
  node('a2', 'Products', '/admin/products'),
];

const ACCOUNT = [
  node('c1', 'My Dashboard', '/account/dashboard'),
  node('c2', 'Purchase History', '/account/orders'),
];

describe('sidebar sections', () => {
  /**
   * The bug this exists for. The sidebar used to filter the menu by the route prefix of
   * whichever layout was mounted, so an administrator granted all eleven menus saw the
   * eight under /admin and had no way to reach the other three.
   */
  it('keeps every granted item, across areas', () => {
    const sections = toSections([...ADMIN, ...ACCOUNT]);

    expect(sections.map((s) => s.label)).toEqual(['Administration', 'Your account']);
    expect(sections.flatMap((s) => s.items)).toHaveLength(4);
  });

  it('drops a section nobody is granted', () => {
    const sections = toSections(ACCOUNT);

    expect(sections).toHaveLength(1);
    expect(sections[0].label).toBe('Your account');
    expect(sections[0].items).toHaveLength(2);
  });

  /**
   * The menu is data, so a route the frontend has never heard of has to appear rather than
   * vanish. Dropping it would make granting a new menu look like it silently failed.
   */
  it('collects an unrecognised route rather than dropping it', () => {
    const sections = toSections([...ADMIN, node('x1', 'Reports', '/reports')]);

    expect(sections.map((s) => s.label)).toEqual(['Administration', 'More']);
    expect(sections.at(-1)?.items.map((i) => i.title)).toEqual(['Reports']);
  });

  it('returns nothing for a role with no menus', () => {
    expect(toSections([])).toEqual([]);
  });

  it('keeps an item with no route out of the named sections', () => {
    const sections = toSections([node('n1', 'Placeholder', null)]);

    expect(sections.map((s) => s.label)).toEqual(['More']);
  });
});
