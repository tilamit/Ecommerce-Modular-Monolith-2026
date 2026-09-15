import { api } from '../../../shared/api/httpClient';
import type { CursorResult, PagedResult } from '../../../shared/api/types';

/** Admin read/write calls (spec §9.1, §9.2, §9.4, §10.1). */

// --- dashboard -------------------------------------------------------------

export interface DailyPoint {
  day: string;
  value: number;
}

export interface AdminDashboard {
  counts: {
    activeUsers: number;
    inactiveUsers: number;
    activeProducts: number;
    inactiveProducts: number;
    activeCategories: number;
    ordersLast30Days: number;
    revenueLast30Days: number;
    currencyCode: string;
  };
  charts: {
    usersRegisteredPerDay: DailyPoint[];
    ordersPerDay: DailyPoint[];
    revenuePerDay: DailyPoint[];
    topProductsByRevenue: { productId: string; productName: string; revenue: number; unitsSold: number }[];
  };
  recentAudit: AuditListItem[];
  generatedUtc: string;
}

export const fetchAdminDashboard = (signal?: AbortSignal) =>
  api.get<AdminDashboard>('/api/v1/dashboard/admin', { signal });

// --- users -----------------------------------------------------------------

export interface UserListItem {
  id: string;
  email: string;
  fullName: string;
  phoneNumber?: string | null;
  isActive: boolean;
  createdUtc: string;
  lastLoginUtc?: string | null;
  roles: string[];
}

export const fetchUsers = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<PagedResult<UserListItem>>(`/api/v1/users?${params}`, { signal });

export const setUserStatus = (id: string, isActive: boolean) =>
  api.patch<void>(`/api/v1/users/${id}/status`, { isActive });

/** Replaces the roles a user holds. The change is recorded in the audit trail with the role names before and after. */
export const setUserRoles = (id: string, roleIds: string[]) =>
  api.put<void>(`/api/v1/users/${id}/roles`, { roleIds });

/** How a user is named in every picker: full name and email, since two people can share a name. */
export const userLabel = (user: Pick<UserListItem, 'fullName' | 'email'>) => `${user.fullName} (${user.email})`;

// --- roles and access ------------------------------------------------------

export interface RoleListItem {
  id: string;
  name: string;
  description?: string | null;
  isSystemRole: boolean;
  isActive: boolean;
  userCount: number;
  permissionCount: number;
}

export interface PermissionListItem {
  id: string;
  code: string;
  displayName: string;
  group: string;
  description?: string | null;
}

export interface MenuAdminNode {
  id: string;
  parentId?: string | null;
  title: string;
  route?: string | null;
  isActive: boolean;
  children: MenuAdminNode[];
}

export const fetchRoles = (signal?: AbortSignal) =>
  api.get<PagedResult<RoleListItem>>('/api/v1/roles?pageSize=100', { signal });

export const fetchPermissions = (signal?: AbortSignal) =>
  api.get<PermissionListItem[]>('/api/v1/permissions', { signal });

export const fetchRolePermissions = (roleId: string, signal?: AbortSignal) =>
  api.get<string[]>(`/api/v1/roles/${roleId}/permissions`, { signal });

export const saveRolePermissions = (roleId: string, permissionIds: string[]) =>
  api.put<void>(`/api/v1/roles/${roleId}/permissions`, { permissionIds });

export const fetchMenuTree = (signal?: AbortSignal) =>
  api.get<MenuAdminNode[]>('/api/v1/menus', { signal });

export const fetchRoleMenus = (roleId: string, signal?: AbortSignal) =>
  api.get<{ menuItemId: string; isVisible: boolean }[]>(`/api/v1/roles/${roleId}/menus`, { signal });

export const saveRoleMenus = (roleId: string, menus: { menuItemId: string; isVisible: boolean }[]) =>
  api.put<void>(`/api/v1/roles/${roleId}/menus`, { menus });

// --- catalog administration ------------------------------------------------

export const setProductStatus = (id: string, isActive: boolean) =>
  api.patch<void>(`/api/v1/catalog/products/${id}/status`, { isActive });

/** One image on a product. `url` is either an uploaded file's route or an external address. */
export interface ProductImageInput {
  url: string;
  altText?: string | null;
  displayOrder: number;
  isPrimary: boolean;
}

export interface ProductWriteRequest {
  categoryId: string;
  sku: string;
  name: string;
  slug?: string | null;
  shortDescription?: string | null;
  description?: string | null;
  price: number;
  compareAtPrice?: number | null;
  currencyCode: string;
  stockQuantity: number;
  isFeatured: boolean;
  images: ProductImageInput[];
}

export const createProduct = (request: ProductWriteRequest) =>
  api.post<{ id: string }>('/api/v1/catalog/products', request);

export const updateProduct = (id: string, request: ProductWriteRequest) =>
  api.put<void>(`/api/v1/catalog/products/${id}`, request);

export const deleteProduct = (id: string) => api.delete<void>(`/api/v1/catalog/products/${id}`);

export interface UploadedImage {
  fileName: string;
  url: string;
  sizeBytes: number;
}

/**
 * Uploads one or more image files in a single request, so attaching a set of images is one
 * action rather than one round trip per file.
 *
 * Goes through the shared client like every other call: it recognises a `FormData` body and
 * leaves the content type to the browser, so the upload still gets token refresh, the
 * correlation header and ProblemDetails error mapping.
 */
export const uploadProductImages = (files: File[]) => {
  const body = new FormData();

  for (const file of files) {
    body.append('files', file);
  }

  return api.post<UploadedImage[]>('/api/v1/catalog/products/images', body);
};

export const createCategory = (request: CategoryWriteRequest) =>
  api.post<{ id: string }>('/api/v1/catalog/categories', request);

export const updateCategory = (id: string, request: CategoryWriteRequest) =>
  api.put<void>(`/api/v1/catalog/categories/${id}`, request);

export const deleteCategory = (id: string) => api.delete<void>(`/api/v1/catalog/categories/${id}`);

export const setCategoryStatus = (id: string, isActive: boolean) =>
  api.patch<void>(`/api/v1/catalog/categories/${id}/status`, { isActive });

export interface CategoryWriteRequest {
  name: string;
  slug?: string | null;
  parentId?: string | null;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
}

export interface CategoryListItem {
  id: string;
  parentId?: string | null;
  name: string;
  slug: string;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
  isActive: boolean;
  productCount: number;
}

export const fetchCategories = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<PagedResult<CategoryListItem>>(`/api/v1/catalog/categories?${params}`, { signal });

/** One category. Opening it is recorded in the audit trail as a read. */
export const fetchCategory = (id: string, signal?: AbortSignal) =>
  api.get<CategoryListItem>(`/api/v1/catalog/categories/${id}`, { signal });

export interface OfferListItem {
  id: string;
  code: string;
  name: string;
  discountType: string;
  discountValue: number;
  startUtc: string;
  endUtc: string;
  minimumOrderAmount?: number | null;
  maxRedemptions?: number | null;
  redemptionCount: number;
  isActive: boolean;
  isRedeemable: boolean;
}

export const fetchOffers = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<PagedResult<OfferListItem>>(`/api/v1/catalog/offers?${params}`, { signal });

/** One offer. Opening it is recorded in the audit trail as a read. */
export const fetchOffer = (id: string, signal?: AbortSignal) =>
  api.get<OfferListItem>(`/api/v1/catalog/offers/${id}`, { signal });

export type DiscountType = 'Percentage' | 'FixedAmount';

export interface OfferWriteRequest {
  code: string;
  name: string;
  discountType: DiscountType;
  discountValue: number;
  startUtc: string;
  endUtc: string;
  minimumOrderAmount: number | null;
  maxRedemptions: number | null;
  isActive: boolean;
}

export const createOffer = (request: OfferWriteRequest) =>
  api.post<OfferListItem>('/api/v1/catalog/offers', request);

export const updateOffer = (id: string, request: OfferWriteRequest) =>
  api.put<OfferListItem>(`/api/v1/catalog/offers/${id}`, request);

/** Soft delete: the offer leaves every list but its row is kept and its code can be reused. */
export const deleteOffer = (id: string) => api.delete<void>(`/api/v1/catalog/offers/${id}`);

// --- audit trail -----------------------------------------------------------

export interface AuditListItem {
  id: number;
  occurredUtc: string;
  action: string;
  module: string;
  entityName: string;
  entityId?: string | null;
  userId?: string | null;
  userName?: string | null;
  screenName?: string | null;
  httpMethod?: string | null;
  path?: string | null;
  correlationId?: string | null;
}

export interface AuditDetail extends AuditListItem {
  userRoles?: string | null;
  userAgent?: string | null;
  ipAddress?: string | null;
  oldValues?: string | null;
  newValues?: string | null;
  changedColumns?: string | null;
}

/**
 * Keyset-paginated (spec §6.5). The caller passes the opaque cursor back rather than a page
 * number, because this table grows fastest and offset paging degrades with depth.
 */
export const fetchAuditTrail = (params: URLSearchParams, signal?: AbortSignal) =>
  api.get<CursorResult<AuditListItem>>(`/api/v1/audit-trails?${params}`, { signal });

export const fetchAuditEntry = (id: number, signal?: AbortSignal) =>
  api.get<AuditDetail>(`/api/v1/audit-trails/${id}`, { signal });
