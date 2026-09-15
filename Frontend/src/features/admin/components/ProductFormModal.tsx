import { useEffect, useState } from 'react';
import {
  createProduct,
  updateProduct,
  type CategoryListItem,
  type ProductImageInput,
  type ProductWriteRequest,
} from '../api';
import { fetchProduct } from '../../catalog';
import { ProductImagesField } from './ProductImagesField';
import { Button } from '../../../shared/components/ui/Button';
import { Input, Select } from '../../../shared/components/ui/Field';
import { Modal } from '../../../shared/components/ui/Modal';
import { Skeleton } from '../../../shared/components/ui/States';
import { useToast } from '../../../shared/components/ui/Toast';
import { ApiError } from '../../../shared/api/httpClient';
import type { ProductListItem } from '../../../shared/api/types';

interface ProductFormModalProps {
  open: boolean;
  /** The row being edited, or null to create. */
  product: ProductListItem | null;
  categories: CategoryListItem[];
  onClose: () => void;
  onSaved: () => void;
}

interface FormState {
  categoryId: string;
  sku: string;
  name: string;
  slug: string;
  shortDescription: string;
  description: string;
  price: string;
  compareAtPrice: string;
  currencyCode: string;
  stockQuantity: string;
  isFeatured: boolean;
}

const EMPTY: FormState = {
  categoryId: '',
  sku: '',
  name: '',
  slug: '',
  shortDescription: '',
  description: '',
  price: '',
  compareAtPrice: '',
  currencyCode: 'USD',
  stockQuantity: '0',
  isFeatured: false,
};

/** Blank optional strings are sent as null, so an emptied field clears rather than storing "". */
const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/**
 * Create and edit for a product, including its images.
 *
 * The list row does not carry every field, so editing loads the full detail first rather
 * than filling half the form from the row and silently blanking the rest on save. Server
 * validation is the authority - the messages it returns are surfaced verbatim rather than
 * duplicating the FluentValidation rules here, where they would drift.
 */
export const ProductFormModal = ({ open, product, categories, onClose, onSaved }: ProductFormModalProps) => {
  const { show } = useToast();

  const [form, setForm] = useState<FormState>(EMPTY);
  const [images, setImages] = useState<ProductImageInput[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isEdit = product !== null;
  const leafCategories = categories.filter((category) => category.isActive);

  useEffect(() => {
    if (!open) {
      return;
    }

    setError(null);

    if (product === null) {
      setForm({ ...EMPTY, categoryId: leafCategories[0]?.id ?? '' });
      setImages([]);
      return;
    }

    setIsLoading(true);

    fetchProduct(product.id)
      .then((detail) => {
        setForm({
          categoryId: detail.categoryId,
          sku: detail.sku,
          name: detail.name,
          slug: detail.slug,
          shortDescription: detail.shortDescription ?? '',
          description: detail.description ?? '',
          price: String(detail.price),
          compareAtPrice: detail.compareAtPrice === null || detail.compareAtPrice === undefined ? '' : String(detail.compareAtPrice),
          currencyCode: detail.currencyCode,
          stockQuantity: String(detail.stockQuantity),
          isFeatured: detail.isFeatured,
        });

        setImages(
          detail.images.map((image, index) => ({
            url: image.url,
            altText: image.altText ?? null,
            displayOrder: index,
            isPrimary: image.isPrimary,
          })),
        );
      })
      .catch(() => setError('That product could not be loaded.'))
      .finally(() => setIsLoading(false));
    // `product.id` is what identifies the row; the object itself is rebuilt on every render
    // of the list, so depending on it would refetch continuously.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, product?.id]);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const submit = async () => {
    setIsSaving(true);
    setError(null);

    const request: ProductWriteRequest = {
      categoryId: form.categoryId,
      sku: form.sku.trim(),
      name: form.name.trim(),
      slug: orNull(form.slug),
      shortDescription: orNull(form.shortDescription),
      description: orNull(form.description),
      price: Number(form.price),
      compareAtPrice: form.compareAtPrice.trim() === '' ? null : Number(form.compareAtPrice),
      currencyCode: form.currencyCode.trim().toUpperCase(),
      stockQuantity: Number(form.stockQuantity),
      isFeatured: form.isFeatured,
      images,
    };

    try {
      if (isEdit) {
        await updateProduct(product.id, request);
      } else {
        await createProduct(request);
      }

      show({ tone: 'success', message: isEdit ? 'Product updated.' : 'Product created.' });
      onSaved();
      onClose();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The product could not be saved.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      size="lg"
      title={isEdit ? 'Edit product' : 'New product'}
      description={isEdit ? product.sku : 'Images can be uploaded or referenced by address.'}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={() => void submit()}
            isLoading={isSaving}
            disabled={isLoading || form.categoryId === ''}
          >
            {isEdit ? 'Save changes' : 'Create product'}
          </Button>
        </>
      }
    >
      {isLoading ? (
        <div className="flex flex-col gap-3" aria-busy="true">
          {Array.from({ length: 6 }, (_, index) => (
            <Skeleton key={index} className="h-10" />
          ))}
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {error !== null && (
            <p role="alert" className="rounded-lg border border-danger/40 bg-danger/5 px-3 py-2 text-sm text-danger">
              {error}
            </p>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Name" required value={form.name} onChange={(e) => set('name', e.target.value)} />
            <Input label="SKU" required value={form.sku} onChange={(e) => set('sku', e.target.value)} />

            <Select
              label="Category"
              required
              value={form.categoryId}
              onChange={(e) => set('categoryId', e.target.value)}
            >
              <option value="" disabled>
                Select a category
              </option>
              {leafCategories.map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </Select>

            <Input
              label="Slug"
              hint="Left blank, one is generated from the name."
              value={form.slug}
              onChange={(e) => set('slug', e.target.value)}
            />

            <Input
              label="Price"
              required
              type="number"
              min={0}
              step="0.01"
              value={form.price}
              onChange={(e) => set('price', e.target.value)}
            />

            <Input
              label="Compare-at price"
              hint="Shown struck through. Must exceed the price."
              type="number"
              min={0}
              step="0.01"
              value={form.compareAtPrice}
              onChange={(e) => set('compareAtPrice', e.target.value)}
            />

            <Input
              label="Currency"
              required
              maxLength={3}
              value={form.currencyCode}
              onChange={(e) => set('currencyCode', e.target.value)}
            />

            <Input
              label="Stock quantity"
              required
              type="number"
              min={0}
              value={form.stockQuantity}
              onChange={(e) => set('stockQuantity', e.target.value)}
            />
          </div>

          <Input
            label="Short description"
            value={form.shortDescription}
            onChange={(e) => set('shortDescription', e.target.value)}
          />

          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-medium text-content">Description</span>
            <textarea
              rows={4}
              value={form.description}
              onChange={(e) => set('description', e.target.value)}
              className="w-full rounded-lg border border-border-subtle bg-surface px-3 py-2 text-sm text-content placeholder:text-content-muted"
            />
          </label>

          <label className="flex items-center gap-2 text-sm text-content">
            <input
              type="checkbox"
              checked={form.isFeatured}
              onChange={(e) => set('isFeatured', e.target.checked)}
              className="size-4 rounded border-border-subtle accent-brand-600"
            />
            Featured on the storefront
          </label>

          <ProductImagesField value={images} onChange={setImages} />
        </div>
      )}
    </Modal>
  );
};
