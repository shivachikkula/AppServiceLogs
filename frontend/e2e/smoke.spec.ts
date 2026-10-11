import { expect, Page, test } from '@playwright/test';

const now = new Date();
const minutesAgo = (minutes: number) => new Date(now.getTime() - minutes * 60_000).toISOString();

const applications = [
  { applicationName: 'Orders API', appKey: 'orders-api' },
  { applicationName: 'Billing', appKey: 'billing' },
];

const exception = {
  itemId: 'ex-1',
  timestamp: minutesAgo(5),
  problemId: 'System.TimeoutException at Orders.Submit',
  type: 'System.TimeoutException',
  message: 'The payment gateway did not answer in time',
  innermostMessage: null,
  method: 'Orders.Submit',
  assembly: 'Orders',
  severityLevel: 3,
  roleName: 'orders-api',
  roleInstance: 'rd0001',
  operationName: 'POST /orders',
  operationId: 'op-1',
  clientType: 'PC',
};

/** Answers every /api call the way the real API would for a signed-in, non-admin user (B2C disabled). */
async function mockApi(page: Page): Promise<string[]> {
  const unexpected: string[] = [];
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    const json = (body: unknown) => route.fulfill({ json: body });
    switch (url.pathname) {
      case '/api/auth-config':
        return json({ enabled: false, clientId: null, authority: null, knownAuthorities: [], scopes: [] });
      case '/api/me':
        return json({ email: 'dev@example.com', name: 'Developer', isAdmin: false });
      case '/api/applications':
        return json(applications);
      case '/api/features':
        return json({ aiQueryEnabled: false });
      case '/api/overview':
        return json({
          rangeMinutes: 60,
          binMinutes: 5,
          kpis: {
            requests: 1200,
            failedRequests: 12,
            avgDurationMs: 84,
            p95DurationMs: 310,
            exceptions: 7,
            failedDependencies: 2,
            previousRequests: 1000,
            previousFailedRequests: 10,
            previousExceptions: 9,
          },
          series: Array.from({ length: 12 }, (_, i) => ({
            timestamp: minutesAgo(60 - i * 5),
            requests: 100,
            failedRequests: i % 4 === 0 ? 3 : 0,
            p95DurationMs: 300,
            exceptions: i % 6 === 0 ? 2 : 0,
          })),
          topFailing: [{ operation: 'POST /orders', failed: 12, total: 400 }],
          slowest: [{ operation: 'GET /reports', count: 30, p95DurationMs: 2400 }],
          topExceptions: [{ type: exception.type, message: exception.message, problemId: exception.problemId, count: 7 }],
        });
      case '/api/logs/live':
        return json({
          items: [
            {
              itemId: 'log-1',
              timestamp: minutesAgo(1),
              ingestedAt: minutesAgo(1),
              itemType: 'trace',
              severityLevel: 1,
              message: 'Order 42 accepted',
              roleName: 'orders-api',
              roleInstance: 'rd0001',
              operationName: 'POST /orders',
              operationId: 'op-2',
              resultCode: null,
              durationMs: null,
              customDimensions: null,
            },
          ],
          cursor: 'c1',
          serverTime: now.toISOString(),
        });
      case '/api/exceptions/summary':
        return json([
          {
            problemId: exception.problemId,
            type: exception.type,
            message: exception.message,
            count: 7,
            affectedOperations: 3,
            firstSeen: minutesAgo(50),
            lastSeen: minutesAgo(5),
          },
        ]);
      case '/api/exceptions':
        return json([exception]);
      default:
        unexpected.push(`${route.request().method()} ${url.pathname}`);
        return route.fulfill({ status: 404, json: { title: 'Not mocked' } });
    }
  });
  return unexpected;
}

test('signed-in user can move between the main pages', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const unexpected = await mockApi(page);

  await page.goto('/');

  // Shell: header, user and the application picker filled from /api/applications.
  await expect(page).toHaveURL(/\/overview$/);
  await expect(page.locator('.brand')).toContainText('OSSE Log Viewer');
  await expect(page.getByText('dev@example.com')).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Application' })).toHaveValue('orders-api');
  await expect(page.getByRole('link', { name: 'Admin' })).toHaveCount(0);

  // Overview renders KPIs and lists from /api/overview.
  await expect(page.getByRole('heading', { name: 'Overview', level: 1 })).toBeVisible();
  await expect(page.locator('.kpi').first()).toBeVisible();
  await expect(page.getByText('POST /orders').first()).toBeVisible();

  // Live logs shows the polled entries.
  await page.getByRole('link', { name: 'Live logs' }).click();
  await expect(page.getByRole('heading', { name: 'Live logs', level: 1 })).toBeVisible();
  await expect(page.getByText('Order 42 accepted').first()).toBeVisible();

  // Exceptions shows the grouped problems and the individual exceptions.
  await page.getByRole('link', { name: 'Exceptions' }).click();
  await expect(page.getByRole('heading', { name: 'Exceptions', level: 1 })).toBeVisible();
  await expect(page.getByText('System.TimeoutException').first()).toBeVisible();
  await expect(page.getByText('The payment gateway did not answer in time').first()).toBeVisible();

  expect(unexpected).toEqual([]);
  expect(errors).toEqual([]);
});

test('admin page is not reachable for a non-admin user', async ({ page }) => {
  await mockApi(page);
  await page.goto('/admin');
  await expect(page).toHaveURL(/\/live$/);
  await expect(page.getByRole('heading', { name: 'Live logs', level: 1 })).toBeVisible();
});
