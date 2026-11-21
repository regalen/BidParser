import { expect, test, type Page, type Route } from '@playwright/test';

const admin = {
  id: 1,
  username: 'admin',
  name: 'Administrator',
  role: 'admin',
  mustChangePassword: false,
  defaultVendor: null,
  fxRate: null,
  margin: null,
  imPercent: null,
};

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

async function mockApi(page: Page, announcement: () => unknown) {
  await page.route('http://127.0.0.1:5173/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/me') return fulfillJson(route, admin);
    if (path === '/api/history') return fulfillJson(route, { rows: [], total: 0 });
    if (path === '/api/parsers') return fulfillJson(route, []);
    if (path === '/api/parse-ui-config') return fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} });
    if (path === '/api/announcement') return fulfillJson(route, announcement());
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"detail":"notFound"}' });
  });
}

test('announcement renders allowlisted markup beneath the header and stays dismissed until its session key changes', async ({ page }) => {
  let dismissalKey = 'session-one';
  await mockApi(page, () => ({
    html: 'Maintenance <strong>Saturday</strong>. <a href="https://example.invalid/status">Status</a><img src=x onerror="window.__xss=1">',
    dismissalKey,
  }));

  await page.goto('/dashboard');
  const banner = page.getByRole('region', { name: 'Announcement' });
  await expect(banner).toBeVisible();
  await expect(page.locator('header').getByRole('region', { name: 'Announcement' })).toBeVisible();
  await expect(banner.locator('strong')).toHaveText('Saturday');
  const link = banner.getByRole('link', { name: 'Status' });
  await expect(link).toHaveAttribute('target', '_blank');
  await expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  await expect(banner.locator('img')).toHaveCount(0);
  expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();

  await banner.getByRole('button', { name: 'Dismiss announcement' }).click();
  await expect(banner).toHaveCount(0);

  await page.reload();
  await expect(page.locator('aside')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Announcement' })).toHaveCount(0);

  // A new login (or a 24-hour rollover, or an admin save) yields a new key from the server.
  dismissalKey = 'session-two';
  await page.reload();
  await expect(page.getByRole('region', { name: 'Announcement' })).toBeVisible();
});

test('no banner renders when no announcement is active', async ({ page }) => {
  await mockApi(page, () => ({ html: null, dismissalKey: null }));
  await page.goto('/dashboard');
  await expect(page.locator('aside')).toBeVisible();
  await expect(page.getByRole('region', { name: 'Announcement' })).toHaveCount(0);
});
