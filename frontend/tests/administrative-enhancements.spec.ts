import { expect, test, type Page, type Route } from '@playwright/test';

const admin = {
  id: 1,
  username: 'admin',
  name: 'Administrator',
  role: 'admin',
  mustChangePassword: false,
  defaultVendor: 'Nutanix',
  fxRate: null,
  margin: null,
  imPercent: null,
};

const parsers = [
  {
    slug: 'nutanix_software_only_pdf',
    displayName: 'Software Only (PDF)',
    vendor: 'Nutanix',
    acceptedMime: 'application/pdf',
    acceptedMimes: ['application/pdf'],
    crmTemplate: 'Foreign Uplift',
    availableTemplates: ['Foreign Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
  {
    slug: 'zebra_pcr_pdf',
    displayName: 'PCR (PDF)',
    vendor: 'Zebra',
    acceptedMime: 'application/pdf',
    acceptedMimes: ['application/pdf'],
    crmTemplate: 'No Calculation',
    availableTemplates: ['No Calculation', 'Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: true,
  },
  ...['Datalogic', 'Epson', 'Strike'].map((vendor) => ({
    slug: `${vendor.toLowerCase()}_quote_pdf`,
    displayName: 'Quote (PDF)',
    vendor,
    acceptedMime: 'application/pdf',
    acceptedMimes: ['application/pdf'],
    crmTemplate: 'No Calculation',
    availableTemplates: ['No Calculation', 'Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: true,
  })),
  {
    slug: 'trellix_quote_xlsm',
    displayName: 'Quote (XLSM)',
    vendor: 'Trellix',
    acceptedMime: 'application/vnd.ms-excel.sheet.macroEnabled.12',
    acceptedMimes: ['application/vnd.ms-excel.sheet.macroEnabled.12'],
    crmTemplate: 'No Calculation',
    availableTemplates: ['No Calculation', 'Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
];

test('Trellix XLSM appears in the file picker and can be selected for upload', async ({ page }) => {
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} });
      return true;
    }
    return false;
  });

  await page.goto('/dashboard');
  await page.locator('aside select').first().selectOption('Trellix');
  await page.getByLabel('File type').selectOption('trellix_quote_xlsm');
  const fileInput = page.locator('input[type="file"]');
  await expect(fileInput).toHaveAttribute('accept', /\.xlsm/);
  await fileInput.setInputFiles('public/samples/Trellix_Quote_900003.xlsm');
  await expect(page.getByText('Trellix_Quote_900003.xlsm', { exact: true })).toBeVisible();
});

test('On Cost follows parser capability across vendors', async ({ page }) => {
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} });
      return true;
    }
    return false;
  });

  await page.goto('/dashboard');
  const vendor = page.locator('aside select').first();
  const onCost = page.getByLabel(/On Cost %/);

  await expect(vendor).toHaveValue('Nutanix');
  await expect(onCost).toHaveCount(0);

  for (const supportingVendor of ['Datalogic', 'Epson', 'Strike', 'Zebra']) {
    await vendor.selectOption(supportingVendor);
    await expect(onCost).toBeVisible();
  }
});

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

async function mockCommonApi(page: Page, handler?: (route: Route, path: string) => Promise<boolean>) {
  await page.route('http://127.0.0.1:5173/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (handler && await handler(route, path)) return;
    if (path === '/api/me') return fulfillJson(route, admin);
    if (path === '/api/history') return fulfillJson(route, { rows: [], total: 0 });
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"detail":"notFound"}' });
  });
}

test('an explicit vendor choice survives a later parse-configuration response', async ({ page }) => {
  let releaseConfiguration!: () => void;
  const configurationGate = new Promise<void>((resolve) => { releaseConfiguration = resolve; });

  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await configurationGate;
      await fulfillJson(route, {
        vendorDefaults: {
          Nutanix: { fxRate: '1.2000', margin: '4.00', imPercent: null, onCostPct: null },
          Zebra: { fxRate: null, margin: null, imPercent: null, onCostPct: '2.85' },
        },
        guidanceByParserSlug: {},
      });
      return true;
    }
    return false;
  });

  await page.goto('/dashboard');
  const vendor = page.locator('aside select').first();
  await expect(vendor).toContainText('Zebra');
  await vendor.selectOption('Zebra');
  const onCost = page.locator('aside input[inputmode="decimal"]').last();
  await onCost.fill('7.25');

  const configurationResponse = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/api/parse-ui-config');
  releaseConfiguration();
  await configurationResponse;
  await page.waitForTimeout(100);

  await expect(vendor).toHaveValue('Zebra');
  await expect(onCost).toHaveValue('7.25');
});

test('runtime configuration editors and feedback expose accessible names', async ({ page }) => {
  const runtimeConfig = {
    guidanceMessages: {
      json: '[{"fileTypes":["retired_slug"],"html":"<p>Repair me</p>"}]',
      updatedAt: null,
      validationError: 'Guidance fileTypes must be unique concrete registered parser slugs.',
    },
    vendorDefaults: { json: '[]', updatedAt: null, validationError: null },
    parsers: [{ slug: 'nutanix_software_only_pdf', displayName: 'Software Only (PDF)', vendor: 'Nutanix' }],
    vendors: ['Nutanix'],
    supportedFields: [{ key: 'fxRate', label: 'FX Rate', scale: 4 }],
    htmlAllowlist: ['p'],
  };

  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/admin/runtime-config') {
      await fulfillJson(route, runtimeConfig);
      return true;
    }
    if (path === '/api/admin/runtime-config/guidance-messages') {
      await fulfillJson(route, { json: '[\n  \n]', updatedAt: '2026-08-17T00:00:00Z', validationError: null });
      return true;
    }
    return false;
  });

  await page.goto('/admin/runtime-config');
  const guidance = page.getByRole('textbox', { name: 'Guidance Messages' });
  const defaults = page.getByRole('textbox', { name: 'Vendor Defaults' });
  await expect(guidance).toBeVisible();
  await expect(defaults).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Stored document needs repair' })).toBeVisible();

  await guidance.fill('{');
  await page.getByRole('button', { name: 'Format JSON' }).first().click();
  await expect(page.getByRole('alert').filter({ hasText: 'Invalid JSON' })).toBeVisible();
  await expect(guidance).toHaveAttribute('aria-invalid', 'true');
  await expect(page.getByRole('list', { name: 'Concrete parser slugs' })).toHaveAttribute('tabindex', '0');

  await guidance.fill('[\n  \n]');
  await page.getByRole('button', { name: 'Save' }).first().click();
  await expect(page.getByRole('status')).toContainText('Guidance messages saved');
});

test('dirty runtime configuration blocks in-app navigation', async ({ page }) => {
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/admin/runtime-config') {
      await fulfillJson(route, {
        guidanceMessages: { json: '[]', updatedAt: null, validationError: null },
        vendorDefaults: { json: '[]', updatedAt: null, validationError: null },
        parsers: [],
        vendors: [],
        supportedFields: [],
        htmlAllowlist: [],
      });
      return true;
    }
    if (path === '/api/users') {
      await fulfillJson(route, []);
      return true;
    }
    return false;
  });

  await page.goto('/admin/runtime-config');
  await page.getByRole('textbox', { name: 'Guidance Messages' }).fill('[ ]');

  page.once('dialog', async (dialog) => {
    expect(dialog.message()).toContain('unsaved runtime configuration changes');
    await dialog.dismiss();
  });
  await page.locator('button[aria-haspopup="menu"]').click();
  await page.getByRole('menuitem', { name: 'Users' }).click();
  await expect(page).toHaveURL(/\/admin\/runtime-config$/);
});
