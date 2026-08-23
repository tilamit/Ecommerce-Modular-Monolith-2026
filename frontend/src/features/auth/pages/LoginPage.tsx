import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { login } from '../api';
import { fetchMe } from '../api';
import { ApiError } from '../../../shared/api/httpClient';
import { Button } from '../../../shared/components/ui/Button';
import { Input } from '../../../shared/components/ui/Field';

const schema = z.object({
  email: z.email('Enter a valid email address'),
  password: z.string().min(1, 'Enter your password'),
});

type LoginForm = z.infer<typeof schema>;

export const LoginPage = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [error, setError] = useState<string | null>(null);

  const form = useForm<LoginForm>({ resolver: zodResolver(schema) });

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);

    try {
      await login(values);
      await fetchMe();

      // Return the user where they were headed, defaulting to the storefront.
      const returnUrl = searchParams.get('returnUrl');
      navigate(returnUrl !== null && returnUrl.startsWith('/') ? returnUrl : '/', { replace: true });
    } catch (caught) {
      // The API answers identically for an unknown email and a wrong password, so the UI
      // must not invent a more specific message and reintroduce the enumeration oracle.
      setError(
        caught instanceof ApiError && caught.status < 500
          ? caught.message
          : 'Could not sign in. Please try again.',
      );
    }
  });

  return (
    <div className="mx-auto flex w-full max-w-sm flex-col gap-6 py-10">
      <div>
        <h1 className="text-2xl font-semibold text-content">Sign in</h1>
        <p className="mt-1 text-sm text-content-muted">Welcome back to ShopHub.</p>
      </div>

      {error !== null && (
        <div role="alert" className="rounded-lg border border-danger/40 bg-danger/10 p-3 text-sm text-content">
          {error}
        </div>
      )}

      <form onSubmit={(event) => void onSubmit(event)} className="flex flex-col gap-4">
        <Input
          label="Email"
          type="email"
          autoComplete="email"
          required
          error={form.formState.errors.email?.message}
          {...form.register('email')}
        />

        <Input
          label="Password"
          type="password"
          autoComplete="current-password"
          required
          error={form.formState.errors.password?.message}
          {...form.register('password')}
        />

        <Button type="submit" size="lg" isLoading={form.formState.isSubmitting}>
          Sign in
        </Button>
      </form>

      <p className="text-sm text-content-muted">
        No account?{' '}
        <Link to="/register" className="font-medium text-brand-600 hover:underline">
          Create one
        </Link>
      </p>
    </div>
  );
};
