import { describe, expect, it } from 'vitest';
import { toDateInput, toOfferRequest, type OfferFormState } from './OfferFormModal';

const form = (overrides: Partial<OfferFormState> = {}): OfferFormState => ({
  code: ' welcome10 ',
  name: ' Welcome discount ',
  discountType: 'Percentage',
  discountValue: '10',
  startDate: '2026-09-01',
  endDate: '2026-09-30',
  minimumOrderAmount: '',
  maxRedemptions: '',
  isActive: true,
  ...overrides,
});

describe('toOfferRequest', () => {
  it('stores the code in capitals and trims the text fields', () => {
    const request = toOfferRequest(form());

    expect(request.code).toBe('WELCOME10');
    expect(request.name).toBe('Welcome discount');
    expect(request.discountValue).toBe(10);
  });

  it('sends a blank minimum or redemption limit as no limit', () => {
    expect(toOfferRequest(form())).toMatchObject({ minimumOrderAmount: null, maxRedemptions: null });
    expect(toOfferRequest(form({ minimumOrderAmount: '200', maxRedemptions: '50' }))).toMatchObject({
      minimumOrderAmount: 200,
      maxRedemptions: 50,
    });
  });

  it('runs from the start of the first day to the end of the last day, sent in UTC', () => {
    const request = toOfferRequest(form());

    expect(request.startUtc).toBe(new Date('2026-09-01T00:00:00').toISOString());
    expect(request.endUtc).toBe(new Date('2026-09-30T23:59:59').toISOString());
    expect(toDateInput(request.startUtc)).toBe('2026-09-01');
    expect(toDateInput(request.endUtc)).toBe('2026-09-30');
  });
});
