import { HttpErrorResponse } from '@angular/common/http';
import { describeError } from './telemetry-api.service';

describe('describeError', () => {
  it('explains a network failure', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach the API');
  });

  it('prefers the ProblemDetails detail, then its title', () => {
    expect(describeError(new HttpErrorResponse({ status: 403, error: { title: 'ApplicationForbidden', detail: 'You do not have access.' } }))).toBe(
      'You do not have access.',
    );
    expect(describeError(new HttpErrorResponse({ status: 429, error: { title: 'TooManyRequests' } }))).toBe('TooManyRequests');
  });

  it('falls back to the status line and to plain errors', () => {
    expect(describeError(new HttpErrorResponse({ status: 500, statusText: 'Internal Server Error' }))).toBe('500 Internal Server Error');
    expect(describeError(new Error('boom'))).toBe('boom');
  });
});
