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
    slug: 'trellix_auto',
    displayName: 'Auto (detect format)',
    vendor: 'Trellix',
    acceptedMime: '',
    acceptedMimes: ['application/pdf', 'application/vnd.ms-excel.sheet.macroEnabled.12'],
    crmTemplate: 'No Calculation',
    availableTemplates: ['No Calculation', 'Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
  {
    slug: 'trellix_quote_pdf',
    displayName: 'Quote (PDF)',
    vendor: 'Trellix',
    acceptedMime: 'application/pdf',
    acceptedMimes: ['application/pdf'],
    crmTemplate: 'No Calculation',
    availableTemplates: ['No Calculation', 'Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
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
  {
    slug: 'juniper_iris_xlsx',
    calculatesCostFromMsrp: true,
    displayName: 'Iris (XLSX)',
    vendor: 'Juniper',
    acceptedMime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    acceptedMimes: ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
    crmTemplate: 'Foreign Uplift',
    availableTemplates: ['Foreign Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
  {
    slug: 'juniper_cpq_xlsx',
    displayName: 'CPQ (XLSX)',
    vendor: 'Juniper',
    acceptedMime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    acceptedMimes: ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
    crmTemplate: 'Foreign Uplift',
    availableTemplates: ['Foreign Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
  {
    slug: 'juniper_indirect_xlsx',
    displayName: 'Indirect (XLSX)',
    vendor: 'Juniper',
    acceptedMime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    acceptedMimes: ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
    crmTemplate: 'Foreign Uplift',
    availableTemplates: ['Foreign Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
  },
];

test('Juniper requires manual selection and preserves pricing while switching Iris, CPQ, and Indirect', async ({ page }) => {
  let parseAttempt = 0;
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} });
      return true;
    }
    if (path === '/api/parse') {
      parseAttempt += 1;
      if (parseAttempt === 1) {
        await route.fulfill({
          status: 200,
          contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          headers: {
            'Content-Disposition': 'attachment; filename="CPQ-900001_1_ForeignUplift.xlsx"',
            'X-Validation': 'match',
            'X-Currency': 'USD',
            'X-Computed-Total': '31072.82',
            'X-Quoted-Total': '31072.82',
            'X-Parser-Slug': 'juniper_cpq_xlsx',
          },
          body: 'mock-xlsx',
        });
        return true;
      }
      if (parseAttempt === 2) {
        await route.fulfill({
          status: 200,
          contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          headers: {
            'Content-Disposition': 'attachment; filename="900001_2_ForeignUplift.xlsx"',
            'X-Validation': 'mismatch',
            'X-Currency': 'USD',
            'X-Computed-Total': '4186.10',
            'X-Quoted-Total': '',
            'X-Validation-Warnings': JSON.stringify([
              'Line 2 (SUB-1S-3Y) computed total 180.00 differs from EXTENDED NET TOTAL 150.00.',
              'Line 3 (<sample> – SKU) computed total 70.00 differs from EXTENDED NET TOTAL 80.00.',
            ]).replace(/[^\x00-\x7f]/g, (character) => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`),
            'X-Parser-Slug': 'juniper_indirect_xlsx',
          },
          body: 'mock-xlsx',
        });
        return true;
      }
      await route.fulfill({
        status: 422,
        contentType: 'application/json',
        body: JSON.stringify({
          detail: {
            stage: 'fileType',
            message:
              'The file is not recognised as Indirect (XLSX) and appears to be a CPQ (XLSX). Select the correct file type and try again.',
          },
        }),
      });
      return true;
    }
    return false;
  });

  await page.goto('/dashboard');
  await page.locator('aside select').first().selectOption('Juniper');
  await expect(page.getByLabel('File type')).toHaveValue('');
  await expect(page.getByLabel('File type').locator('option')).toHaveCount(4);

  await page.getByLabel('File type').selectOption('juniper_cpq_xlsx');
  await expect(page.getByLabel('CRM Import Template')).toHaveCount(0);

  const fxInput = page.getByLabel(/Exchange rate/i);
  const upliftInput = page.getByLabel(/^Uplift/i);
  await expect(fxInput).toBeVisible();
  await expect(upliftInput).toBeVisible();

  const fileInput = page.locator('input[type="file"]');
  await fileInput.setInputFiles('public/samples/Juniper_CPQ_900001.xlsx');
  await expect(page.getByText('Juniper_CPQ_900001.xlsx', { exact: true })).toBeVisible();

  const submitButton = page.getByRole('button', { name: 'Upload & parse' });
  await expect(submitButton).toBeDisabled();

  await fxInput.fill('0.6500');
  await expect(submitButton).toBeDisabled();

  await upliftInput.fill('5.00');
  await expect(submitButton).toBeEnabled();

  await page.getByLabel('File type').selectOption('juniper_iris_xlsx');
  const imInput = page.getByLabel(/IM% \(Discount Off MSRP\)/i);
  await expect(imInput).toBeVisible();
  await expect(submitButton).toBeDisabled();
  await imInput.fill('1.234');
  await expect(submitButton).toBeDisabled();
  await imInput.fill('-1');
  await expect(submitButton).toBeDisabled();
  await imInput.fill('130.25');
  await expect(submitButton).toBeEnabled();
  await imInput.fill('0');
  await expect(submitButton).toBeEnabled();
  await page.getByLabel('File type').selectOption('juniper_cpq_xlsx');
  await expect(imInput).toHaveCount(0);
  await expect(fxInput).toHaveValue('0.6500');
  await expect(upliftInput).toHaveValue('5.00');

  // Parse the manually selected CPQ format and download.
  await submitButton.click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('Parse successful');
  await expect(dialog).not.toContainText('Detected file type:');
  const download1Promise = page.waitForEvent('download');
  await dialog.getByRole('button', { name: 'Download' }).click();
  const download1 = await download1Promise;
  expect(download1.suggestedFilename()).toBe('CPQ-900001_1_ForeignUplift.xlsx');
  await dialog.getByRole('button', { name: 'Close' }).click();

  // Parse Indirect + Foreign Uplift with a saveable per-row mismatch
  await page.getByLabel('File type').selectOption('juniper_indirect_xlsx');
  await fileInput.setInputFiles('public/samples/Juniper_Indirect_900001.xlsx');
  await submitButton.click();
  await expect(dialog).toContainText('Parsed with warnings');
  await expect(dialog).toContainText('Computed USD 4186.10');
  await expect(dialog).not.toContainText('Quoted USD');
  await expect(dialog).not.toContainText('not parsed fully');
  await expect(dialog.getByRole('list', { name: 'Validation warnings' })).toContainText('Line 2 (SUB-1S-3Y)');
  await expect(dialog.getByRole('list', { name: 'Validation warnings' })).toContainText('Line 3 (<sample> – SKU)');
  const download2Promise = page.waitForEvent('download');
  await dialog.getByRole('button', { name: 'Download' }).click();
  const download2 = await download2Promise;
  expect(download2.suggestedFilename()).toBe('900001_2_ForeignUplift.xlsx');
  await dialog.getByRole('button', { name: 'Close' }).click();

  // 3. Wrong-format suggestion modal
  await fileInput.setInputFiles('public/samples/Juniper_CPQ_900001.xlsx');
  await submitButton.click();
  const alertDialog = page.getByRole('alertdialog');
  await expect(alertDialog).toContainText(
    'The file is not recognised as Indirect (XLSX) and appears to be a CPQ (XLSX). Select the correct file type and try again.',
  );
  await alertDialog.getByRole('button', { name: 'OK' }).click();
});

test('Iris submits all three pricing inputs and presents the explicit download', async ({ page }) => {
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') { await fulfillJson(route, parsers); return true; }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} }); return true;
    }
    if (path !== '/api/parse') return false;
    const body = route.request().postDataBuffer()!.toString();
    for (const value of ['juniper_iris_xlsx', 'Foreign Uplift', 'imPercent', '27.53', 'fxRate', '1.5000', 'margin', '5.00']) {
      expect(body).toContain(value);
    }
    await route.fulfill({
      status: 200, contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      headers: {
        'Content-Disposition': 'attachment; filename="Iris_ForeignUplift.xlsx"',
        'X-Validation': 'match', 'X-Currency': 'USD', 'X-Computed-Total': '1100.00',
        'X-Quoted-Total': '1100.00', 'X-Parser-Slug': 'juniper_iris_xlsx',
      }, body: 'mock-xlsx',
    });
    return true;
  });
  await page.goto('/dashboard');
  await page.locator('aside select').first().selectOption('Juniper');
  await page.getByLabel('File type').selectOption('juniper_iris_xlsx');
  await page.locator('input[type="file"]').setInputFiles('public/samples/Juniper_Iris_Synthetic.xlsx');
  await page.getByLabel(/Exchange rate/i).fill('1.5000');
  await page.getByLabel(/^Uplift/i).fill('5.00');
  await page.getByLabel(/IM% \(Discount Off MSRP\)/i).fill('27.53');
  await page.getByRole('button', { name: 'Upload & parse' }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toContainText('Parse successful');
  const download = page.waitForEvent('download');
  await dialog.getByRole('button', { name: 'Download' }).click();
  expect((await download).suggestedFilename()).toBe('Iris_ForeignUplift.xlsx');
});

test('currency errors display the server guidance for USD and AUD', async ({ page }) => {
  let hint = '';
  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, { vendorDefaults: {}, guidanceByParserSlug: {} });
      return true;
    }
    if (path !== '/api/parse') return false;
    await route.fulfill({
      status: 422, contentType: 'application/json',
      body: JSON.stringify({ detail: { stage: 'currency', hint, message: 'Unsupported currency.' } }),
    });
    return true;
  });

  await page.goto('/dashboard');
  for (const [vendor, template, extension, guidance] of [
    ['Juniper', 'Foreign Uplift', 'xlsx', 'This Juniper Indirect quote declares AUD on line 1; USD is required.'],
    ['Trellix', 'No Calculation', 'xlsm', 'This Trellix quote declares USD; AUD is required.'],
  ]) {
    hint = guidance;
    await page.locator('aside select').first().selectOption(vendor);
    if (vendor === 'Juniper') {
      await page.getByLabel('File type').selectOption('juniper_indirect_xlsx');
      await page.getByLabel(/Exchange rate/i).fill('1.5');
      await page.getByLabel(/^Uplift/i).fill('5');
    } else {
      await page.getByLabel('CRM Import Template').selectOption(template);
    }
    await page.locator('input[type="file"]').setInputFiles({
      name: `synthetic.${extension}`, mimeType: 'application/octet-stream', buffer: Buffer.from('synthetic'),
    });
    await page.getByRole('button', { name: 'Upload & parse' }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText(guidance);
    await expect(dialog).not.toContainText('please upload an AUD version');
    await dialog.getByRole('button', { name: 'OK' }).click();
  }
});

test('Trellix defaults to Auto and accepts PDF and XLSM uploads', async ({ page }) => {
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
  await expect(page.getByLabel('File type')).toHaveValue('trellix_auto');
  await expect(page.getByLabel('File type').locator('option')).toHaveCount(4);
  const fileInput = page.locator('input[type="file"]');
  await expect(fileInput).toHaveAttribute('accept', /\.pdf.*\.xlsm/);
  await fileInput.setInputFiles('public/samples/Trellix_Quote_900001.pdf');
  await expect(page.getByText('Trellix_Quote_900001.pdf', { exact: true })).toBeVisible();
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
