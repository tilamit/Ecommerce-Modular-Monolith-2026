import { api } from '../../../shared/api/httpClient';
import type { PagedResult } from '../../../shared/api/types';

export interface OrderRow {
  id: string;
  orderNumber: string;
  status: string;
  grandTotal: number;
  currencyCode: string;
  placedUtc: string;
  itemCount: number;
  customerType: string;
  customerName: string;
  customerEmail: string;
  paymentMethod: string;
}

export interface OrderDetail extends OrderRow {
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  shippingTotal: number;
  appliedOfferCode?: string | null;
  paymentStatus: string;
  notes?: string | null;
  shippingAddress: {
    fullName: string;
    line1: string;
    line2?: string | null;
    city: string;
    state?: string | null;
    postalCode: string;
    country: string;
    phoneNumber?: string | null;
  };
  items: {
    productId: string;
    productName: string;
    sku: string;
    unitPrice: number;
    quantity: number;
    discountAmount: number;
    lineTotal: number;
  }[];
  statusHistory: {
    fromStatus?: string | null;
    toStatus: string;
    changedUtc: string;
    changedByUserId?: string | null;
    reason?: string | null;
  }[];
}

// --- admin: every order ----------------------------------------------------

export const fetchAllOrders = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<PagedResult<OrderRow>>(`/api/v1/orders?${params}`, { signal });

export const fetchOrder = (id: string, signal?: AbortSignal) =>
  api.get<OrderDetail>(`/api/v1/orders/${id}`, { signal });

export const changeOrderStatus = (id: string, status: string, reason?: string) =>
  api.patch<OrderDetail>(`/api/v1/orders/${id}/status`, { status, reason });

// --- customer: own orders only ---------------------------------------------

/**
 * These endpoints take no user id at all (spec §7.5). Ownership is enforced by the server
 * against the token, so there is nothing here for a caller to tamper with.
 */
export const fetchMyOrders = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<PagedResult<OrderRow>>(`/api/v1/orders/me?${params}`, { signal });

export const fetchMyOrder = (id: string, signal?: AbortSignal) =>
  api.get<OrderDetail>(`/api/v1/orders/me/${id}`, { signal });

/** Allowed while Pending or Confirmed; admin-only afterwards (ADR-003). */
export const cancelMyOrder = (id: string, reason?: string) =>
  api.post<OrderDetail>(`/api/v1/orders/me/${id}/cancel`, { reason });

export interface CustomerDashboard {
  counts: {
    totalOrders: number;
    totalSpent: number;
    ordersLast30Days: number;
    spentLast30Days: number;
    pendingOrders: number;
    currencyCode: string;
  };
  charts: {
    spendPerDay: { day: string; value: number }[];
    ordersPerDay: { day: string; value: number }[];
  };
  recentOrders: {
    id: string;
    orderNumber: string;
    placedUtc: string;
    status: string;
    grandTotal: number;
    currencyCode: string;
    itemCount: number;
  }[];
}

export const fetchCustomerDashboard = (signal?: AbortSignal) =>
  api.get<CustomerDashboard>('/api/v1/dashboard/customer', { signal });
