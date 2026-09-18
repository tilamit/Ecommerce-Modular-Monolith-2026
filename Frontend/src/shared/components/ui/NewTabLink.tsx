import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { ExternalLink } from 'lucide-react';
import { cn } from '../../lib/cn';

interface NewTabLinkProps {
  to: string;
  /** The destination in words. Shown as the link text, and the basis of its accessible name. */
  label: string;
  /** Custom visual content. Defaults to `label`, truncated to the width it is given. */
  children?: ReactNode;
  /** Set false where the surrounding layout already signals the link leaves the page. */
  showIcon?: boolean;
  className?: string;
}

/**
 * An in-app link that opens in a new browser tab.
 *
 * Three details are easy to omit and all three are required, which is why this exists once
 * rather than at each call site:
 *
 * - `rel="noopener"` denies the opened page a `window.opener` handle back into this one,
 *   which is otherwise enough to navigate the original tab elsewhere while the reader is
 *   looking at the new one. `noreferrer` covers browsers that ignore `noopener`.
 * - The new tab is announced. A link that moves focus to a new context without saying so is
 *   WCAG 3.2.5: the icon says it to sighted readers, the accessible name to everyone else.
 * - The announcement is an `aria-label` built from `label` rather than a visually hidden
 *   sibling span. The accessible name is computed by concatenating the text nodes with no
 *   separator, so a sibling announces as "Blue Mug(opens in a new tab)". Building the whole
 *   name in one place is the only way to control it. It still begins with the visible text,
 *   which is what voice control needs to match on.
 */
export const NewTabLink = ({ to, label, children, showIcon = true, className }: NewTabLinkProps) => (
  <Link
    to={to}
    target="_blank"
    rel="noopener noreferrer"
    aria-label={`${label} (opens in a new tab)`}
    className={cn('inline-flex items-center gap-1 hover:underline', className)}
  >
    {children ?? <span className="min-w-0 truncate">{label}</span>}
    {showIcon && <ExternalLink className="size-3.5 shrink-0" aria-hidden="true" />}
  </Link>
);
