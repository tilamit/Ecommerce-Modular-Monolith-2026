import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

/**
 * Merges class names, with later Tailwind utilities beating earlier ones.
 *
 * Without twMerge, `cn('p-2', 'p-4')` emits both and the winner depends on stylesheet
 * order rather than on the caller's intent - which makes component variants unpredictable.
 */
export const cn = (...inputs: ClassValue[]): string => twMerge(clsx(inputs));
