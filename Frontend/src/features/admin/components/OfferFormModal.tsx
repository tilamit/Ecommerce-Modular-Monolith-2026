import { useEffect, useState } from 'react';
import {
  createOffer,
  fetchOffer,
  updateOffer,
  type DiscountType,
  type OfferListItem,
  type OfferWriteRequest,
} from '../api';
import { Button } from '../../../shared/components/ui/Button';
import { Input, Select } from '../../../shared/components/ui/Field';
import { Modal } from '../../../shared/components/ui/Modal';
import { useToast } from '../../../shared/components/ui/Toast';
import { ApiError } from '../../../shared/api/httpClient';

interface OfferFormModalProps {
  open: boolean;
  /** The row being edited, or null to create an offer. */
  offer: OfferListItem | null;
  onClose: () => void;
  onSaved: () => void;
}

export interface OfferFormState {
  code: string;
  name: string;
  discountType: DiscountType;
  discountValue: string;
  startDate: string;
  endDate: string;
  minimumOrderAmount: string;
  maxRedemptions: string;
  isActive: boolean;
}

const pad = (value: number) => String(value).padStart(2, '0');

/** A UTC timestamp as the `yyyy-mm-dd` a date input shows, in the browser's own time zone. */
export const toDateInput = (utc: string) => {
  const date = new Date(utc);

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
};

const today = () => toDateInput(new Date().toISOString());

const inDays = (days: number) => toDateInput(new Date(Date.now() + days * 86_400_000).toISOString());

const EMPTY = (): OfferFormState => ({
  code: '',
  name: '',
  discountType: 'Percentage',
  discountValue: '',
  startDate: today(),
  endDate: inDays(30),
  minimumOrderAmount: '',
  maxRedemptions: '',
  isActive: true,
});

const optionalNumber = (value: string) => (value.trim() === '' ? null : Number(value));

/**
 * Builds the request from the form. An offer starts at the beginning of its first day and ends
 * at the end of its last day, both in the browser's time zone. Both are sent to the API in UTC.
 */
export const toOfferRequest = (form: OfferFormState): OfferWriteRequest => ({
  code: form.code.trim().toUpperCase(),
  name: form.name.trim(),
  discountType: form.discountType,
  discountValue: Number(form.discountValue),
  startUtc: new Date(`${form.startDate}T00:00:00`).toISOString(),
  endUtc: new Date(`${form.endDate}T23:59:59`).toISOString(),
  minimumOrderAmount: optionalNumber(form.minimumOrderAmount),
  maxRedemptions: optionalNumber(form.maxRedemptions),
  isActive: form.isActive,
});

/**
 * Create and edit for a discount offer.
 *
 * Editing loads the offer from the API rather than reusing the list row, so the form shows
 * what is stored now and opening it is recorded in the audit trail as a read. Server
 * validation is the authority and its messages are shown as they come back.
 */
export const OfferFormModal = ({ open, offer, onClose, onSaved }: OfferFormModalProps) => {
  const { show } = useToast();

  const [form, setForm] = useState<OfferFormState>(EMPTY);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isEdit = offer !== null;

  useEffect(() => {
    if (!open) {
      return;
    }

    setError(null);

    if (offer === null) {
      setForm(EMPTY());
      return;
    }

    setIsLoading(true);

    fetchOffer(offer.id)
      .then((detail) =>
        setForm({
          code: detail.code,
          name: detail.name,
          discountType: detail.discountType === 'FixedAmount' ? 'FixedAmount' : 'Percentage',
          discountValue: String(detail.discountValue),
          startDate: toDateInput(detail.startUtc),
          endDate: toDateInput(detail.endUtc),
          minimumOrderAmount: detail.minimumOrderAmount === null || detail.minimumOrderAmount === undefined ? '' : String(detail.minimumOrderAmount),
          maxRedemptions: detail.maxRedemptions === null || detail.maxRedemptions === undefined ? '' : String(detail.maxRedemptions),
          isActive: detail.isActive,
        }),
      )
      .catch(() => setError('That offer could not be loaded.'))
      .finally(() => setIsLoading(false));
    // `offer.id` identifies the row; the object is rebuilt on every render of the list.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, offer?.id]);

  const set = <K extends keyof OfferFormState>(key: K, value: OfferFormState[K]) =>
    setForm((current) => ({ ...current, [key]: value }));

  const submit = async () => {
    setIsSaving(true);
    setError(null);

    try {
      if (isEdit) {
        await updateOffer(offer.id, toOfferRequest(form));
      } else {
        await createOffer(toOfferRequest(form));
      }

      show({ tone: 'success', message: isEdit ? 'Offer updated.' : 'Offer created.' });
      onSaved();
      onClose();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The offer could not be saved.');
    } finally {
      setIsSaving(false);
    }
  };

  const canSubmit =
    !isLoading && form.code.trim() !== '' && form.name.trim() !== '' && form.discountValue.trim() !== '';

  return (
    <Modal
      open={open}
      title={isEdit ? 'Edit offer' : 'New offer'}
      description={isEdit ? offer.code : 'Customers enter the code at checkout.'}
      onClose={onClose}
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose}>
            Cancel
          </Button>
          <Button size="sm" onClick={() => void submit()} isLoading={isSaving} disabled={!canSubmit}>
            {isEdit ? 'Save changes' : 'Create offer'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4" aria-busy={isLoading}>
        {error !== null && (
          <p role="alert" className="rounded-lg border border-danger/40 bg-danger/5 px-3 py-2 text-sm text-danger">
            {error}
          </p>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Code"
            required
            hint="Stored in capitals."
            value={form.code}
            onChange={(e) => set('code', e.target.value)}
          />
          <Input label="Name" required value={form.name} onChange={(e) => set('name', e.target.value)} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Select
            label="Discount type"
            value={form.discountType}
            onChange={(e) => set('discountType', e.target.value as DiscountType)}
          >
            <option value="Percentage">Percentage</option>
            <option value="FixedAmount">Fixed amount</option>
          </Select>
          <Input
            label={form.discountType === 'Percentage' ? 'Discount (%)' : 'Discount amount'}
            type="number"
            required
            min="0.01"
            step="0.01"
            value={form.discountValue}
            onChange={(e) => set('discountValue', e.target.value)}
          />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Starts"
            type="date"
            required
            value={form.startDate}
            onChange={(e) => set('startDate', e.target.value)}
          />
          <Input
            label="Ends"
            type="date"
            required
            value={form.endDate}
            onChange={(e) => set('endDate', e.target.value)}
          />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="Minimum order amount"
            type="number"
            min="0.01"
            step="0.01"
            hint="Optional."
            value={form.minimumOrderAmount}
            onChange={(e) => set('minimumOrderAmount', e.target.value)}
          />
          <Input
            label="Maximum redemptions"
            type="number"
            min="1"
            step="1"
            hint="Optional. Left blank, there is no limit."
            value={form.maxRedemptions}
            onChange={(e) => set('maxRedemptions', e.target.value)}
          />
        </div>

        {isEdit && (
          <label className="flex items-center gap-2 text-sm text-content">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => set('isActive', e.target.checked)}
              className="size-4 rounded border-border-subtle"
            />
            Active
          </label>
        )}
      </div>
    </Modal>
  );
};
