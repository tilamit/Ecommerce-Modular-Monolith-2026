import { describe, expect, it, vi, afterEach } from 'vitest';
import { createQueryClient } from './providers';
import { ApiError } from '../shared/api/httpClient';

/**
 * The retry policy is what stands between a first run and the "Something went wrong" panel
 * on a first run: a cold API start takes about thirty seconds, Vite takes one, and
 * everything the storefront asks for in between goes unanswered.
 */
const unreachable = () =>
  new ApiError(0, { status: 0, title: 'The server could not be reached.', code: 'service_unreachable' });

const empty = () =>
  new ApiError(200, { status: 200, title: 'The server returned an empty response.', code: 'empty_response' });

describe('query client retry policy', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('waits out an API that is not listening yet', async () => {
    vi.useFakeTimers();

    const queryFn = vi
      .fn()
      .mockRejectedValueOnce(unreachable())
      .mockRejectedValueOnce(unreachable())
      .mockResolvedValue('products');

    const result = createQueryClient().fetchQuery({ queryKey: ['catalogue'], queryFn });

    // Two retries at the fast half-second cadence: the page fills a second in, not thirty.
    await vi.advanceTimersByTimeAsync(1_500);

    await expect(result).resolves.toBe('products');
    expect(queryFn).toHaveBeenCalledTimes(3);
  });

  it('gives up on an API that never comes up, rather than retrying forever', async () => {
    vi.useFakeTimers();

    const queryFn = vi.fn().mockRejectedValue(unreachable());

    // Caught up front rather than with `.rejects`: the rejection lands while the timers are
    // being advanced and an unattached rejected promise is an unhandled rejection.
    const settled = createQueryClient()
      .fetchQuery({ queryKey: ['catalogue'], queryFn })
      .catch((error: unknown) => error);

    // Six attempts half a second apart, then eighteen two seconds apart: ~39 seconds.
    await vi.advanceTimersByTimeAsync(60_000);

    expect(await settled).toBeInstanceOf(ApiError);
    expect(queryFn).toHaveBeenCalledTimes(25);
  });

  /**
   * The empty 200 an output-cached endpoint hands a request that joined a cancelled one.
   * Nothing has to recover for the next attempt to work, so this is the one failure the
   * policy retries without waiting.
   */
  it('asks again immediately when the server answers with nothing', async () => {
    vi.useFakeTimers();

    const queryFn = vi.fn().mockRejectedValueOnce(empty()).mockResolvedValue('products');

    const result = createQueryClient().fetchQuery({ queryKey: ['catalogue'], queryFn });

    await vi.advanceTimersByTimeAsync(300);

    await expect(result).resolves.toBe('products');
    expect(queryFn).toHaveBeenCalledTimes(2);
  });

  it('stops asking if every answer is empty', async () => {
    vi.useFakeTimers();

    const queryFn = vi.fn().mockRejectedValue(empty());

    const settled = createQueryClient()
      .fetchQuery({ queryKey: ['catalogue'], queryFn })
      .catch((error: unknown) => error);

    await vi.advanceTimersByTimeAsync(5_000);

    expect(await settled).toBeInstanceOf(ApiError);
    expect(queryFn).toHaveBeenCalledTimes(4);
  });

  it('still refuses to retry an ordinary client error', async () => {
    const queryFn = vi.fn().mockRejectedValue(new ApiError(404, { status: 404, code: 'not_found' }));

    await expect(
      createQueryClient().fetchQuery({ queryKey: ['missing'], queryFn }),
    ).rejects.toBeInstanceOf(ApiError);

    expect(queryFn).toHaveBeenCalledTimes(1);
  });
});
