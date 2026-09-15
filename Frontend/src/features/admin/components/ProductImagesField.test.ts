import { describe, expect, it } from 'vitest';
import { normaliseProductImages } from './ProductImagesField';
import type { ProductImageInput } from '../api';

const image = (url: string, isPrimary = false): ProductImageInput => ({
  url,
  altText: null,
  displayOrder: 0,
  isPrimary,
});

/**
 * Two invariants the product form must not be able to break one edit at a time: the stored
 * order has to match what the admin sees and a product with images has to have exactly one
 * primary. Deriving both from the array position means no individual handler carries them.
 */
describe('product image normalisation', () => {
  it('numbers display order from the array position', () => {
    const result = normaliseProductImages([image('a'), image('b'), image('c')]);

    expect(result.map((i) => i.displayOrder)).toEqual([0, 1, 2]);
  });

  it('makes the first image primary when none is', () => {
    const result = normaliseProductImages([image('a'), image('b')]);

    expect(result.map((i) => i.isPrimary)).toEqual([true, false]);
  });

  it('keeps the primary that is already set', () => {
    const result = normaliseProductImages([image('a'), image('b', true), image('c')]);

    expect(result.map((i) => i.isPrimary)).toEqual([false, true, false]);
  });

  /** Removing the primary must promote another, not leave the product without one. */
  it('promotes a new primary when the primary is removed', () => {
    const remaining = [image('b'), image('c')];

    expect(normaliseProductImages(remaining).filter((i) => i.isPrimary)).toHaveLength(1);
  });

  it('never leaves two primaries', () => {
    const result = normaliseProductImages([image('a', true), image('b', true)]);

    expect(result.filter((i) => i.isPrimary)).toHaveLength(1);
  });

  it('leaves an empty list alone', () => {
    expect(normaliseProductImages([])).toEqual([]);
  });

  /** Reordering has to renumber, or the saved order disagrees with the rendered one. */
  it('renumbers after a swap', () => {
    const swapped = [image('b'), image('a', true)];
    const result = normaliseProductImages(swapped);

    expect(result.map((i) => [i.url, i.displayOrder, i.isPrimary])).toEqual([
      ['b', 0, false],
      ['a', 1, true],
    ]);
  });
});
