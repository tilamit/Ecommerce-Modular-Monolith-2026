import { useEffect, useState } from 'react';
import { createCategory, updateCategory, type CategoryListItem, type CategoryWriteRequest } from '../api';
import { Button } from '../../../shared/components/ui/Button';
import { Input, Select } from '../../../shared/components/ui/Field';
import { Modal } from '../../../shared/components/ui/Modal';
import { useToast } from '../../../shared/components/ui/Toast';
import { ApiError } from '../../../shared/api/httpClient';

interface CategoryFormModalProps {
  open: boolean;
  category: CategoryListItem | null;
  /** Every category, so one can be chosen as the parent. */
  categories: CategoryListItem[];
  onClose: () => void;
  onSaved: () => void;
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/**
 * Create and edit for a category.
 *
 * The list row carries every editable field, so unlike the product form this needs no
 * detail fetch. The parent list excludes the category being edited: a category that is its
 * own parent is a cycle the tree builder would recurse on forever.
 */
export const CategoryFormModal = ({ open, category, categories, onClose, onSaved }: CategoryFormModalProps) => {
  const { show } = useToast();

  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [parentId, setParentId] = useState('');
  const [description, setDescription] = useState('');
  const [imageUrl, setImageUrl] = useState('');
  const [displayOrder, setDisplayOrder] = useState('0');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isEdit = category !== null;

  useEffect(() => {
    if (!open) {
      return;
    }

    setError(null);
    setName(category?.name ?? '');
    setSlug(category?.slug ?? '');
    setParentId(category?.parentId ?? '');
    setDescription(category?.description ?? '');
    setImageUrl(category?.imageUrl ?? '');
    setDisplayOrder(String(category?.displayOrder ?? 0));
  }, [open, category]);

  const submit = async () => {
    setIsSaving(true);
    setError(null);

    const request: CategoryWriteRequest = {
      name: name.trim(),
      slug: orNull(slug),
      parentId: parentId === '' ? null : parentId,
      description: orNull(description),
      imageUrl: orNull(imageUrl),
      displayOrder: Number(displayOrder),
    };

    try {
      if (isEdit) {
        await updateCategory(category.id, request);
      } else {
        await createCategory(request);
      }

      show({ tone: 'success', message: isEdit ? 'Category updated.' : 'Category created.' });
      onSaved();
      onClose();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The category could not be saved.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal
      open={open}
      title={isEdit ? 'Edit category' : 'New category'}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button size="sm" onClick={() => void submit()} isLoading={isSaving} disabled={name.trim() === ''}>
            {isEdit ? 'Save changes' : 'Create category'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error !== null && (
          <p role="alert" className="rounded-lg border border-danger/40 bg-danger/5 px-3 py-2 text-sm text-danger">
            {error}
          </p>
        )}

        <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} />

        <Input
          label="Slug"
          hint="Left blank, one is generated from the name."
          value={slug}
          onChange={(e) => setSlug(e.target.value)}
        />

        <Select label="Parent" value={parentId} onChange={(e) => setParentId(e.target.value)}>
          <option value="">None, a top-level category</option>
          {categories
            .filter((option) => option.id !== category?.id)
            .map((option) => (
              <option key={option.id} value={option.id}>
                {option.name}
              </option>
            ))}
        </Select>

        <Input label="Description" value={description} onChange={(e) => setDescription(e.target.value)} />

        <Input
          label="Image address"
          type="url"
          placeholder="https://example.com/category.jpg"
          value={imageUrl}
          onChange={(e) => setImageUrl(e.target.value)}
        />

        <Input
          label="Display order"
          type="number"
          hint="Lower sorts first."
          value={displayOrder}
          onChange={(e) => setDisplayOrder(e.target.value)}
        />
      </div>
    </Modal>
  );
};
