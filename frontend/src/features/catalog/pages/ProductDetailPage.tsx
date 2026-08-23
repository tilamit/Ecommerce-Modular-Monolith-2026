import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { ShoppingCart, ChevronRight } from 'lucide-react';
import { fetchProduct } from '../api';
import { useCartStore } from '../../cart';
import { useToast } from '../../../shared/components/ui/Toast';
import { Button } from '../../../shared/components/ui/Button';
import { ErrorState, Skeleton } from '../../../shared/components/ui/States';
import { discountPercent, formatCurrency } from '../../../shared/lib/format';

export const ProductDetailPage = () => {
  const { slug = '' } = useParams();
  const [activeImage, setActiveImage] = useState(0);
  const addToCart = useCartStore((state) => state.add);
  const removeFromCart = useCartStore((state) => state.remove);
  const { show } = useToast();

  const productQuery = useQuery({
    queryKey: ['products', 'detail', slug],
    queryFn: ({ signal }) => fetchProduct(slug, signal),
    enabled: slug.length > 0,
  });

  if (productQuery.isPending) {
    return (
      <div aria-busy="true" className="grid gap-8 md:grid-cols-2">
        <Skeleton className="aspect-square w-full" />
        <div className="flex flex-col gap-4">
          <Skeleton className="h-8 w-3/4" />
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-12 w-40" />
        </div>
      </div>
    );
  }

  if (productQuery.isError) {
    return (
      <ErrorState
        title="Product not found"
        description="This product may have been removed or is no longer available."
        onRetry={() => void productQuery.refetch()}
      />
    );
  }

  const product = productQuery.data;
  const discount = discountPercent(product.price, product.compareAtPrice);
  const isOutOfStock = product.stockQuantity <= 0;
  const images = product.images.length > 0 ? product.images : [];

  return (
    <div className="flex flex-col gap-6">
      <nav aria-label="Breadcrumb">
        <ol className="flex items-center gap-1 text-sm text-content-muted">
          <li>
            <Link to="/" className="hover:text-content hover:underline">
              Shop
            </Link>
          </li>
          <ChevronRight className="size-4" aria-hidden="true" />
          <li>
            <Link to={`/?categoryIds=${product.categoryId}`} className="hover:text-content hover:underline">
              {product.categoryName}
            </Link>
          </li>
          <ChevronRight className="size-4" aria-hidden="true" />
          <li aria-current="page" className="truncate text-content">
            {product.name}
          </li>
        </ol>
      </nav>

      <div className="grid gap-8 md:grid-cols-2">
        <div className="flex flex-col gap-3">
          <div className="overflow-hidden rounded-card bg-surface-sunken">
            {images[activeImage] !== undefined ? (
              <img
                src={images[activeImage].url}
                alt={images[activeImage].altText ?? product.name}
                width={640}
                height={640}
                className="aspect-square w-full object-cover"
              />
            ) : (
              <div className="aspect-square w-full" aria-hidden="true" />
            )}
          </div>

          {images.length > 1 && (
            <div className="flex gap-2" role="group" aria-label="Product images">
              {images.map((image, index) => (
                <button
                  key={image.url}
                  type="button"
                  onClick={() => setActiveImage(index)}
                  aria-label={`Show image ${index + 1} of ${images.length}`}
                  aria-current={index === activeImage}
                  className={`size-16 overflow-hidden rounded-lg border-2 ${
                    index === activeImage ? 'border-brand-500' : 'border-transparent'
                  }`}
                >
                  <img src={image.url} alt="" width={64} height={64} loading="lazy" className="size-full object-cover" />
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="flex flex-col gap-4">
          <div>
            <p className="text-sm text-content-muted">{product.categoryName}</p>
            <h1 className="mt-1 text-2xl font-semibold text-content">{product.name}</h1>
            <p className="mt-1 text-xs text-content-muted">SKU {product.sku}</p>
          </div>

          <div className="flex items-baseline gap-3">
            <span className="text-3xl font-semibold text-content">
              {formatCurrency(product.price, product.currencyCode)}
            </span>
            {discount !== null && product.compareAtPrice !== null && product.compareAtPrice !== undefined && (
              <>
                <span className="text-lg text-content-muted line-through">
                  {formatCurrency(product.compareAtPrice, product.currencyCode)}
                </span>
                <span className="rounded-full bg-danger px-2 py-0.5 text-xs font-semibold text-white">
                  Save {discount}%
                </span>
              </>
            )}
          </div>

          <p aria-live="polite" className={isOutOfStock ? 'text-sm text-danger' : 'text-sm text-success'}>
            {isOutOfStock ? 'Out of stock' : `In stock - ${product.stockQuantity} available`}
          </p>

          {product.shortDescription !== null && product.shortDescription !== undefined && (
            <p className="text-sm text-content-muted">{product.shortDescription}</p>
          )}

          <Button
            size="lg"
            disabled={isOutOfStock}
            onClick={() => {
              addToCart(product.id, 1);
              show({
                tone: 'success',
                message: `${product.name} added to your cart.`,
                action: { label: 'Undo', onClick: () => removeFromCart(product.id) },
              });
            }}
            leadingIcon={<ShoppingCart className="size-5" aria-hidden="true" />}
            className="self-start"
          >
            Add to cart
          </Button>

          {product.description !== null && product.description !== undefined && (
            <section className="mt-2 border-t border-border-subtle pt-4">
              <h2 className="mb-2 text-sm font-semibold text-content">Description</h2>
              <p className="whitespace-pre-line text-sm text-content-muted">{product.description}</p>
            </section>
          )}
        </div>
      </div>
    </div>
  );
};
