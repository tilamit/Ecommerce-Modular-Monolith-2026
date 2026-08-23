import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { fetchMe, register as registerAccount } from '../api';
import { ApiError } from '../../../shared/api/httpClient';
import { Button } from '../../../shared/components/ui/Button';
import { Input } from '../../../shared/components/ui/Field';

// Length is what correlates with password strength, so the floor is 8 rather than a
// composition rule that mostly produces "Password1!". Matches the API's own validator.
const schema = z.object({
  email: z.email('Enter a valid email address'),
  password: z.string().min(8, 'Use at least 8 characters'),
  firstName: z.string().min(1, 'Required').max(100),
  lastName: z.string().min(1, 'Required').max(100),
  phoneNumber: z.string().max(32).optional(),
});

type RegisterForm = z.infer<typeof schema>;

export const RegisterPage = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [error, setError] = useState<string | null>(null);

  const form = useForm<RegisterForm>({ resolver: zodResolver(schema) });

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);

    try {
      await registerAccount(values);
      await fetchMe();

      const returnUrl = searchParams.get('returnUrl');
      navigate(returnUrl !== null && returnUrl.startsWith('/') ? returnUrl : '/', { replace: true });
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.code === 'email_taken'
          ? 'An account with that email already exists.'
          : 'Could not create your account. Please try again.',
      );
    }
  });

  return (
    <div className="mx-auto flex w-full max-w-sm flex-col gap-6 py-10">
      <div>
        <h1 className="text-2xl font-semibold text-content">Create an account</h1>
        <p className="mt-1 text-sm text-content-muted">Your cart comes with you.</p>
      </div>

      {error !== null && (
        <div role="alert" className="rounded-lg border border-danger/40 bg-danger/10 p-3 text-sm text-content">
          {error}
        </div>
      )}

      <form onSubmit={(event) => void onSubmit(event)} className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Input
            label="First name"
            autoComplete="given-name"
            required
            error={form.formState.errors.firstName?.message}
            {...form.register('firstName')}
          />
          <Input
            label="Last name"
            autoComplete="family-name"
            required
            error={form.formState.errors.lastName?.message}
            {...form.register('lastName')}
          />
        </div>

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
          autoComplete="new-password"
          required
          hint="At least 8 characters."
          error={form.formState.errors.password?.message}
          {...form.register('password')}
        />

        <Input label="Phone number" type="tel" autoComplete="tel" {...form.register('phoneNumber')} />

        <Button type="submit" size="lg" isLoading={form.formState.isSubmitting}>
          Create account
        </Button>
      </form>

      <p className="text-sm text-content-muted">
        Already have an account?{' '}
        <Link to="/login" className="font-medium text-brand-600 hover:underline">
          Sign in
        </Link>
      </p>
    </div>
  );
};
