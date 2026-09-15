/**
 * Permission codes, mirroring the backend's Permissions class (spec §7.5).
 *
 * Constants rather than inline strings so a typo is a compile error rather than a route
 * that silently never authorises. These gate *navigation* only - every endpoint enforces
 * its own permission server-side, because client-side hiding is UX, not security.
 */
export const Permissions = {
  ProductsRead: 'catalog.products.read',
  ProductsWrite: 'catalog.products.write',
  CategoriesRead: 'catalog.categories.read',
  CategoriesWrite: 'catalog.categories.write',
  OffersRead: 'catalog.offers.read',
  OffersWrite: 'catalog.offers.write',
  UsersRead: 'identity.users.read',
  UsersManage: 'identity.users.manage',
  RolesRead: 'identity.roles.read',
  RolesManage: 'identity.roles.manage',
  AccessManage: 'identity.access.manage',
  OrdersReadAll: 'orders.read.all',
  OrdersReadOwn: 'orders.read.own',
  OrdersManage: 'orders.manage',
  AuditRead: 'audit.read',
  DashboardAdmin: 'dashboard.admin',
  DashboardOwn: 'dashboard.own',
} as const;
