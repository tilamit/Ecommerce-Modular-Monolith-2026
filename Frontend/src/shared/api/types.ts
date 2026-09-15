/** Shapes returned by the API. Hand-written to match the backend contracts (spec §11.3). */

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

/** Keyset-paginated envelope, used by the audit trail (spec §6.5). */
export interface CursorResult<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

/** RFC 9457 ProblemDetails as this API emits it (spec §6.1). */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** Stable machine-readable code. Branch on this, never on the message. */
  code?: string;
  traceId?: string;
  /** Field-keyed validation messages, present on a 400. */
  errors?: Record<string, string[]>;
  /** Per-line stock failures, present on a checkout 409 (ADR-002). */
  failures?: ReservationFailure[];
}

export interface ReservationFailure {
  productId: string;
  productName: string;
  reason: string;
  available: number;
  requested: number;
}

export interface UserProfile {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  phoneNumber?: string | null;
  isActive: boolean;
  roles: string[];
  permissions: string[];
}

/**
 * The login/refresh payload (spec A1).
 *
 * Note what is absent: the refresh token. It arrives as an HttpOnly cookie that JavaScript
 * cannot read, so the SPA only ever learns when it expires.
 */
export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresUtc: string;
  refreshTokenExpiresUtc: string;
  user: UserProfile;
}

export interface MenuNode {
  id: string;
  title: string;
  icon?: string | null;
  route?: string | null;
  displayOrder: number;
  children: MenuNode[];
}

export interface MeResponse {
  profile: UserProfile;
  menu: MenuNode[];
}

// --- catalog ---------------------------------------------------------------

export interface ProductListItem {
  id: string;
  sku: string;
  name: string;
  slug: string;
  shortDescription?: string | null;
  price: number;
  compareAtPrice?: number | null;
  currencyCode: string;
  stockQuantity: number;
  isActive: boolean;
  isFeatured: boolean;
  rating?: number | null;
  reviewCount: number;
  categoryId: string;
  categoryName: string;
  primaryImageUrl?: string | null;
  createdUtc: string;
}

export interface ProductImage {
  url: string;
  altText?: string | null;
  displayOrder: number;
  isPrimary: boolean;
}

export interface ProductDetail extends Omit<ProductListItem, 'primaryImageUrl'> {
  description?: string | null;
  images: ProductImage[];
  modifiedUtc?: string | null;
}

export interface ProductSuggestion {
  id: string;
  name: string;
  slug: string;
  price: number;
  currencyCode: string;
  imageUrl?: string | null;
}

export interface CategoryNode {
  id: string;
  parentId?: string | null;
  name: string;
  slug: string;
  description?: string | null;
  imageUrl?: string | null;
  displayOrder: number;
  isActive: boolean;
  productCount: number;
  children: CategoryNode[];
}

// --- cart and checkout -----------------------------------------------------

export interface CartLine {
  productId: string;
  name: string;
  sku: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  availableStock: number;
  /** True when the server reduced the quantity to what is actually in stock. */
  quantityWasClamped: boolean;
}

/** A line the server could not honour and why (spec §8.4). */
export interface SkippedLine {
  productId: string;
  name?: string | null;
  reason: string;
}

export interface Cart {
  cartId?: string | null;
  items: CartLine[];
  skipped: SkippedLine[];
  subTotal: number;
  currencyCode: string;
  totalQuantity: number;
}

export interface AddressInput {
  fullName: string;
  line1: string;
  line2?: string;
  city: string;
  state?: string;
  postalCode: string;
  country: string;
  phoneNumber?: string;
}

export type PaymentMethod = 'CashOnDelivery' | 'Card' | 'BankTransfer';

export interface CheckoutResponse {
  orderId: string;
  orderNumber: string;
  status: string;
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  shippingTotal: number;
  grandTotal: number;
  currencyCode: string;
  paymentMethod: string;
  paymentStatus: string;
  placedUtc: string;
  skipped: SkippedLine[];
  /** Set when a guest email already has an account - a prompt, never an auto-link (spec §8.4). */
  accountExistsPrompt?: string | null;
}

// --- orders ----------------------------------------------------------------

export interface OrderListItem {
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
