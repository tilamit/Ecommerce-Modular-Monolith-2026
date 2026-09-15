/** Orders' public surface (spec §11.1). */
export {
  fetchAllOrders,
  fetchOrder,
  changeOrderStatus,
  fetchMyOrders,
  fetchMyOrder,
  cancelMyOrder,
  fetchCustomerDashboard,
} from './api';
export type { OrderRow, OrderDetail, CustomerDashboard } from './api';
