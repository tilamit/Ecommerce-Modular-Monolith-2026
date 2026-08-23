import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';

/**
 * The frontend mirror of the backend's architecture tests (spec §11.1).
 *
 * "A feature folder may import from `shared/` and from its own folder. Cross-feature
 * imports go through a feature's `index.ts` public surface - same discipline as the backend
 * `Contracts` projects."
 *
 * The oxlint `no-restricted-imports` rule covers the common shapes, but it matches on glob
 * patterns and so depends on how deeply a file happens to be nested. This test resolves
 * every import to an absolute path and decides from that, which cannot be sidestepped by
 * adding another `../`.
 */

const FEATURES_DIR = path.resolve(import.meta.dirname, '..', 'features');

const sourceFiles = (dir: string): string[] =>
  readdirSync(dir).flatMap((entry) => {
    const full = path.join(dir, entry);

    if (statSync(full).isDirectory()) {
      return sourceFiles(full);
    }

    return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) ? [full] : [];
  });

const IMPORT_PATTERN = /(?:from|import)\s+['"]([^'"]+)['"]/g;

const featureOf = (file: string): string => path.relative(FEATURES_DIR, file).split(path.sep)[0];

describe('feature boundaries', () => {
  const files = sourceFiles(FEATURES_DIR);

  it('finds the feature sources it is meant to police', () => {
    // Guards against the suite silently passing because the glob stopped matching.
    expect(files.length).toBeGreaterThan(5);
  });

  it('never reaches into another feature below its public surface', () => {
    const violations: string[] = [];

    for (const file of files) {
      const owner = featureOf(file);
      const contents = readFileSync(file, 'utf8');

      for (const match of contents.matchAll(IMPORT_PATTERN)) {
        const specifier = match[1];

        if (!specifier.startsWith('.')) {
          continue;
        }

        const resolved = path.resolve(path.dirname(file), specifier);

        if (!resolved.startsWith(FEATURES_DIR)) {
          continue;
        }

        const target = featureOf(resolved);

        if (target === owner) {
          continue;
        }

        // Cross-feature: the only legal target is the feature root or its index.
        const withinTarget = path.relative(path.join(FEATURES_DIR, target), resolved);
        const isPublicSurface = withinTarget === '' || withinTarget === 'index' || withinTarget === 'index.ts';

        if (!isPublicSurface) {
          violations.push(
            `${path.relative(FEATURES_DIR, file)} imports '${specifier}' from feature '${target}'`,
          );
        }
      }
    }

    expect(violations, violations.join('\n')).toEqual([]);
  });
});
