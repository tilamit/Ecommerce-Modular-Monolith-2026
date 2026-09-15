import { useId, useRef, useState } from 'react';
import { ArrowDown, ArrowUp, Link2, Star, Trash2, Upload } from 'lucide-react';
import { uploadProductImages, type ProductImageInput } from '../api';
import { Button } from '../../../shared/components/ui/Button';
import { useToast } from '../../../shared/components/ui/Toast';
import { ApiError } from '../../../shared/api/httpClient';
import { cn } from '../../../shared/lib/cn';

interface ProductImagesFieldProps {
  value: ProductImageInput[];
  onChange: (images: ProductImageInput[]) => void;
}

/**
 * Renumbers `displayOrder` from the array position and guarantees exactly one primary.
 *
 * Both properties are easy to break one edit at a time - delete the primary and a product
 * has none, reorder and the stored order disagrees with what the admin sees. Deriving them
 * from the array on every change means no individual handler has to remember either rule.
 */
const normalise = (images: ProductImageInput[]): ProductImageInput[] => {
  const primary = images.findIndex((image) => image.isPrimary);
  const primaryIndex = primary === -1 ? 0 : primary;

  return images.map((image, index) => ({
    ...image,
    displayOrder: index,
    isPrimary: index === primaryIndex,
  }));
};

/**
 * Attaches images to a product, by upload or by address.
 *
 * Both routes exist because they answer different needs. An uploaded file is stored by the
 * API and referenced by a route it owns, which is what an admin without a CDN needs. An
 * external URL costs nothing to store and is how the seeded catalogue is populated. The
 * product does not care which it is holding: the database column is a URL either way.
 */
export const ProductImagesField = ({ value, onChange }: ProductImagesFieldProps) => {
  const fileInput = useRef<HTMLInputElement>(null);
  const urlFieldId = useId();
  const { show } = useToast();

  const [urlDraft, setUrlDraft] = useState('');
  const [isUploading, setIsUploading] = useState(false);

  const append = (images: ProductImageInput[]) => onChange(normalise([...value, ...images]));

  const upload = async (files: FileList | null) => {
    if (files === null || files.length === 0) {
      return;
    }

    setIsUploading(true);

    try {
      // One request for the whole selection, so a set of images is one action.
      const stored = await uploadProductImages([...files]);

      append(
        stored.map((image, index) => ({
          url: image.url,
          altText: null,
          displayOrder: value.length + index,
          isPrimary: false,
        })),
      );

      show({ tone: 'success', message: `${stored.length} image${stored.length === 1 ? '' : 's'} uploaded.` });
    } catch (error) {
      show({
        tone: 'error',
        message: error instanceof ApiError ? error.message : 'The images could not be uploaded.',
      });
    } finally {
      setIsUploading(false);

      // Cleared so selecting the same file twice in a row still fires a change event.
      if (fileInput.current !== null) {
        fileInput.current.value = '';
      }
    }
  };

  const addUrl = () => {
    const url = urlDraft.trim();

    if (url === '') {
      return;
    }

    append([{ url, altText: null, displayOrder: value.length, isPrimary: false }]);
    setUrlDraft('');
  };

  const move = (index: number, by: -1 | 1) => {
    const target = index + by;

    if (target < 0 || target >= value.length) {
      return;
    }

    const next = [...value];
    [next[index], next[target]] = [next[target], next[index]];

    onChange(normalise(next));
  };

  const remove = (index: number) => onChange(normalise(value.filter((_, i) => i !== index)));

  const setPrimary = (index: number) =>
    onChange(normalise(value.map((image, i) => ({ ...image, isPrimary: i === index }))));

  const setAltText = (index: number, altText: string) =>
    onChange(value.map((image, i) => (i === index ? { ...image, altText: altText || null } : image)));

  return (
    <fieldset className="flex flex-col gap-3">
      <legend className="mb-1 text-xs font-semibold uppercase tracking-wide text-content-muted">
        Images
      </legend>

      <div className="flex flex-wrap items-end gap-2">
        <div>
          <input
            ref={fileInput}
            type="file"
            accept="image/png,image/jpeg,image/webp,image/gif,image/avif"
            multiple
            onChange={(event) => void upload(event.target.files)}
            className="sr-only"
            id={`${urlFieldId}-files`}
          />
          <Button
            type="button"
            variant="secondary"
            size="sm"
            isLoading={isUploading}
            onClick={() => fileInput.current?.click()}
          >
            <Upload className="size-4" aria-hidden="true" />
            Upload images
          </Button>
        </div>

        <div className="flex flex-1 items-end gap-2">
          <div className="min-w-0 flex-1">
            <label htmlFor={urlFieldId} className="mb-1 block text-xs text-content-muted">
              Or paste an image address
            </label>
            <input
              id={urlFieldId}
              type="url"
              value={urlDraft}
              onChange={(event) => setUrlDraft(event.target.value)}
              onKeyDown={(event) => {
                // Enter inside a form would submit the product; this field adds an image.
                if (event.key === 'Enter') {
                  event.preventDefault();
                  addUrl();
                }
              }}
              placeholder="https://example.com/photo.jpg"
              className="h-9 w-full rounded-lg border border-border-subtle bg-surface px-3 text-sm text-content placeholder:text-content-muted"
            />
          </div>

          <Button type="button" variant="secondary" size="sm" onClick={addUrl} disabled={urlDraft.trim() === ''}>
            <Link2 className="size-4" aria-hidden="true" />
            Add
          </Button>
        </div>
      </div>

      {value.length === 0 ? (
        <p className="rounded-lg border border-dashed border-border-subtle px-3 py-6 text-center text-sm text-content-muted">
          No images yet. A product without one still saves.
        </p>
      ) : (
        <ul className="flex flex-col gap-2">
          {value.map((image, index) => (
            <li
              key={`${image.url}-${index}`}
              className={cn(
                'flex items-center gap-3 rounded-lg border p-2',
                image.isPrimary ? 'border-brand-600' : 'border-border-subtle',
              )}
            >
              {/*
                The address may be an upload route or somewhere external and either can be
                wrong. A broken image hides the row it belongs to, so the failure is caught
                and shown as a placeholder that keeps the controls reachable.
              */}
              <img
                src={image.url}
                alt=""
                className="size-14 shrink-0 rounded-md object-cover"
                onError={(event) => {
                  event.currentTarget.style.visibility = 'hidden';
                }}
              />

              <div className="flex min-w-0 flex-1 flex-col gap-1">
                <span className="truncate font-mono text-xs text-content-muted">{image.url}</span>

                <label className="flex items-center gap-2 text-xs text-content-muted">
                  <span className="sr-only">Alt text for image {index + 1}</span>
                  <input
                    type="text"
                    value={image.altText ?? ''}
                    onChange={(event) => setAltText(index, event.target.value)}
                    placeholder="Alt text, for screen readers"
                    className="h-8 w-full rounded-lg border border-border-subtle bg-surface px-2 text-xs text-content placeholder:text-content-muted"
                  />
                </label>
              </div>

              <div className="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  onClick={() => setPrimary(index)}
                  aria-pressed={image.isPrimary}
                  aria-label={`Make image ${index + 1} the primary image`}
                  title="Primary image"
                  className={cn(
                    'rounded-lg p-1.5 transition-colors hover:bg-surface-sunken',
                    image.isPrimary ? 'text-brand-600' : 'text-content-muted',
                  )}
                >
                  <Star className={cn('size-4', image.isPrimary && 'fill-current')} aria-hidden="true" />
                </button>

                <button
                  type="button"
                  onClick={() => move(index, -1)}
                  disabled={index === 0}
                  aria-label={`Move image ${index + 1} up`}
                  className="rounded-lg p-1.5 text-content-muted transition-colors hover:bg-surface-sunken disabled:opacity-30"
                >
                  <ArrowUp className="size-4" aria-hidden="true" />
                </button>

                <button
                  type="button"
                  onClick={() => move(index, 1)}
                  disabled={index === value.length - 1}
                  aria-label={`Move image ${index + 1} down`}
                  className="rounded-lg p-1.5 text-content-muted transition-colors hover:bg-surface-sunken disabled:opacity-30"
                >
                  <ArrowDown className="size-4" aria-hidden="true" />
                </button>

                <button
                  type="button"
                  onClick={() => remove(index)}
                  aria-label={`Remove image ${index + 1}`}
                  className="rounded-lg p-1.5 text-content-muted transition-colors hover:bg-surface-sunken hover:text-danger"
                >
                  <Trash2 className="size-4" aria-hidden="true" />
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </fieldset>
  );
};

export { normalise as normaliseProductImages };
