import { isRouteErrorResponse, useNavigate, useRouteError } from 'react-router';
import { Button } from '../shared/components/ui/Button';

/**
 * Catches render and loader errors so a thrown exception shows a recoverable page rather
 * than a blank screen (spec §11.1).
 */
export const RouteErrorBoundary = () => {
  const error = useRouteError();
  const navigate = useNavigate();

  const title = isRouteErrorResponse(error) ? `${error.status} ${error.statusText}` : 'Something went wrong';

  return (
    <div role="alert" className="mx-auto flex max-w-md flex-col items-center gap-4 py-20 text-center">
      <h1 className="text-2xl font-semibold text-content">{title}</h1>
      <p className="text-sm text-content-muted">
        The page could not be displayed. Going back to the storefront usually helps.
      </p>
      <Button onClick={() => navigate('/')}>Back to shop</Button>
    </div>
  );
};
