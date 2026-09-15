import { useQuery } from '@tanstack/react-query';
import { fetchNewArrivals } from '../api';
import { ProductCard } from '../components/ProductCard';
import { useCartStore } from '../../cart';
import { useToast } from '../../../shared/components/ui/Toast';
import { EmptyState, ErrorState, ProductCardSkeleton } from '../../../shared/components/ui/States';

/** Products added in the last 30 days (spec §9.2). */
export const NewArrivalsPage = () => {
  const addToCart = useCartStore((state) => state.add);
  const removeFromCart = useCartStore((state) => state.remove);
  const { show } = useToast();

  const query = useQuery({
    queryKey: ['products', 'new'],
    queryFn: ({ signal }) => fetchNewArrivals(24, signal),
  });

  return (
    <section className="flex flex-col gap-4">
      <div>
        <h1 className="text-xl font-semibold text-content">New arrivals</h1>
        <p className="text-sm text-content-muted">Everything added in the last 30 days.</p>
      </div>

      {query.isPending ? (
        <div aria-busy="true" className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-4">
          {Array.from({ length: 8 }, (_, index) => (
            <ProductCardSkeleton key={index} />
          ))}
        </div>
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState title="Nothing new just yet" description="Check back soon." />
      ) : (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-4">
          {query.data.items.map((product) => (
            <ProductCard
              key={product.id}
              product={product}
              onAddToCart={(p) => {
                addToCart(p.id, 1);
                show({
                  tone: 'success',
                  message: `${p.name} added to your cart.`,
                  action: { label: 'Undo', onClick: () => removeFromCart(p.id) },
                });
              }}
            />
          ))}
        </div>
      )}
    </section>
  );
};
