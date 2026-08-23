import { Link } from 'react-router';
import { ShoppingCart } from 'lucide-react';
import type { ProductListItem } from '../../../shared/api/types';
import { discountPercent, formatCurrency } from '../../../shared/lib/format';
import { Button } from '../../../shared/components/ui/Button';

interface ProductCardProps {
  product: ProductListItem;
  onAddToCart: (product: ProductListItem) => void;
}

export const ProductCard = ({ product, onAddToCart }: ProductCardProps) => {
  const discount = discountPercent(product.price, product.compareAtPrice);
  const isOutOfStock = product.stockQuantity <= 0;

  return (
    <article className="group flex flex-col overflow-hidden rounded-card border border-border-subtle bg-surface-raised transition-shadow hover:shadow-md">
      <Link to={`/products/${product.slug}`} className="relative block overflow-hidden bg-surface-sunken">
        {product.primaryImageUrl !== null && product.primaryImageUrl !== undefined ? (
          <img
            src={product.primaryImageUrl}
            alt={product.name}
            // Explicit dimensions and lazy loading prevent layout shift and needless
            // fetches below the fold (spec §11.4).
            width={640}
            height={640}
            loading="lazy"
            decoding="async"
            className="aspect-square w-full object-cover transition-transform duration-200 group-hover:scale-[1.03]"
          />
        ) : (
          <div className="aspect-square w-full" aria-hidden="true" />
        )}

        {discount !== null && (
          <span className="absolute left-2 top-2 rounded-full bg-danger px-2 py-0.5 text-xs font-semibold text-white">
            −{discount}%
          </span>
        )}

        {isOutOfStock && (
          <span className="absolute right-2 top-2 rounded-full bg-surface px-2 py-0.5 text-xs font-medium text-content-muted">
            Out of stock
          </span>
        )}
      </Link>

      <div className="flex flex-1 flex-col gap-2 p-3">
        <p className="text-xs text-content-muted">{product.categoryName}</p>

        <h3 className="line-clamp-2 text-sm font-medium text-content">
          <Link to={`/products/${product.slug}`} className="hover:underline">
            {product.name}
          </Link>
        </h3>

        <div className="mt-auto flex items-baseline gap-2">
          <span className="text-base font-semibold text-content">
            {formatCurrency(product.price, product.currencyCode)}
          </span>

          {discount !== null && product.compareAtPrice !== null && product.compareAtPrice !== undefined && (
            <span className="text-sm text-content-muted line-through">
              {formatCurrency(product.compareAtPrice, product.currencyCode)}
            </span>
          )}
        </div>

        <Button
          size="sm"
          disabled={isOutOfStock}
          onClick={() => onAddToCart(product)}
          leadingIcon={<ShoppingCart className="size-4" aria-hidden="true" />}
          // The visible label is just "Add" for layout; the accessible name names the product.
          aria-label={`Add ${product.name} to cart`}
        >
          Add to cart
        </Button>
      </div>
    </article>
  );
};
