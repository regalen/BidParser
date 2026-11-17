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

const parsers = [
  {
    slug: 'juniper_iris_xlsx',
    displayName: 'Iris (XLSX)',
    vendor: 'Juniper',
    acceptedMime: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    acceptedMimes: ['application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
    crmTemplate: 'Foreign Uplift',
    availableTemplates: ['Foreign Uplift'],
    supportsSubComponentDetail: false,
    supportsSolutionIdSplit: false,
    supportsOnCost: false,
    calculatesCostFromMsrp: true,
    supportsLinePricing: true,
    solutionSplitLabel: 'Solution ID',
  },
];

const r5Preview = {
  parserSlug: 'juniper_iris_xlsx',
  parserDisplayName: 'Iris (XLSX)',
  currency: 'USD',
  crmTemplate: 'Foreign Uplift',
  usesFxRate: true,
  imPercentEditable: true,
  imPercentRequired: true,
  validation: {
    matches: true,
    computedTotal: '1281.00',
    quotedTotal: '1281.00',
    warnings: [],
  },
  lines: [
    {
      index: 0,
      lineNumber: '1',
      vpn: 'EX4400-48MP',
      description:
        'EX4400 48-port 100BASE-T/1000BASE-T/2.5GbE/5GbE/10GbE PoE++ switch with 4x SFP+ and 2x QSFP28 uplink ports, 1x AC PSU, back-to-front airflow',
      qty: 2,
      msrp: '427',
      sourceCost: null,
      derivedImPercent: null,
      isCancelled: false,
    },
    {
      index: 1,
      lineNumber: '2',
      vpn: 'SUB-EX-48-A1-3',
      description: 'EX4400 48-port Standard subscription license, 3 year term',
      qty: 1,
      msrp: '427',
      sourceCost: null,
      derivedImPercent: null,
      isCancelled: false,
    },
  ],
};

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
}

async function mockCommonApi(page: Page, handler: (route: Route, path: string) => Promise<boolean>) {
  await page.route('http://127.0.0.1:5173/api/**', async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (await handler(route, path)) return;
    if (path === '/api/me') return fulfillJson(route, admin);
    if (path === '/api/history') return fulfillJson(route, { rows: [], total: 0 });
    await route.fulfill({ status: 404, contentType: 'application/json', body: '{"detail":"notFound"}' });
  });
}

test('per-line pricing modal applies header defaults, honours row edits, computes R5 totals, and submits linePricing payload', async ({
  page,
}) => {
  let submittedBody = '';

  await mockCommonApi(page, async (route, path) => {
    if (path === '/api/parsers') {
      await fulfillJson(route, parsers);
      return true;
    }
    if (path === '/api/parse-ui-config') {
      await fulfillJson(route, {
        vendorDefaults: {},
        guidanceByParserSlug: {
          juniper_iris_xlsx: '<p>Administrator guidance for Juniper Iris imports appears here.</p>',
        },
      });
      return true;
    }
    if (path === '/api/parse/preview') {
      await fulfillJson(route, r5Preview);
      return true;
    }
    if (path === '/api/parse') {
      submittedBody = route.request().postDataBuffer()!.toString();
      await route.fulfill({
        status: 200,
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        headers: {
          'Content-Disposition': 'attachment; filename="Iris_Quote_Synthetic_ForeignUplift.xlsx"',
          'X-Validation': 'match',
          'X-Currency': 'USD',
          'X-Computed-Total': '1035.35',
          'X-Quoted-Total': '1281.00',
          'X-Parser-Slug': 'juniper_iris_xlsx',
        },
        body: 'mock-xlsx-bytes',
      });
      return true;
    }
    return false;
  });

  await page.goto('/dashboard');

  // 1. Main page hides Exchange rate / Uplift / IM% inputs for Juniper and shows per-line note
  await expect(page.locator('aside select').first()).toHaveValue('Juniper');
  await expect(page.getByLabel('File type')).toHaveValue('juniper_iris_xlsx');
  await expect(page.locator('aside').getByLabel(/Exchange rate/i)).toHaveCount(0);
  await expect(page.locator('aside').getByLabel(/^Uplift/i)).toHaveCount(0);
  await expect(page.locator('aside').getByLabel(/IM%/i)).toHaveCount(0);
  await expect(page.locator('aside')).toContainText('Exchange rate, IM% and Uplift are set per line after you upload.');

  // 2. Attach file and open modal
  await page.locator('input[type="file"]').setInputFiles('public/samples/Juniper_Iris_Synthetic.xlsx');
  await page.getByRole('button', { name: 'Upload & parse' }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('Line Pricing Editor');
  await expect(dialog).toContainText('Bid Currency: USD');

  // 3. Description truncates and exposes full text via title
  const firstDescCell = dialog.locator('tbody tr').first().locator('td').nth(2);
  await expect(firstDescCell).toHaveClass(/truncate/);
  await expect(firstDescCell).toHaveAttribute(
    'title',
    'EX4400 48-port 100BASE-T/1000BASE-T/2.5GbE/5GbE/10GbE PoE++ switch with 4x SFP+ and 2x QSFP28 uplink ports, 1x AC PSU, back-to-front airflow',
  );

  // Submit starts disabled until FX and every line are valid
  const submitButton = dialog.getByRole('button', { name: 'Submit' });
  await expect(submitButton).toBeDisabled();

  // 4. Fill header FX, IM%, and Uplift, then Apply to all
  await dialog.getByLabel('FX (AUD:USD)').fill('0.6520');
  await dialog.getByLabel('IM% · Discount off MSRP').fill('15.00');
  await dialog.getByRole('button', { name: 'Apply IM% to all' }).click();
  await dialog.getByLabel('Uplift %', { exact: true }).fill('4.76');
  await dialog.getByRole('button', { name: 'Apply Uplift to all' }).click();

  await expect(dialog.getByLabel('Line 1 IM%')).toHaveValue('15.00');
  await expect(dialog.getByLabel('Line 2 IM%')).toHaveValue('15.00');
  await expect(dialog.getByLabel('Line 1 Uplift%')).toHaveValue('4.76');
  await expect(dialog.getByLabel('Line 2 Uplift%')).toHaveValue('4.76');
  await expect(submitButton).toBeEnabled();

  // Check R4 values on row 1 (MSRP 427, qty 2, IM 15, uplift 4.76 => unitCost 362.95, extCost 725.90, unitPrice 381.09, extPrice 762.18)
  const row1 = dialog.locator('tbody tr').nth(0);
  await expect(row1).toContainText('362.95');
  await expect(row1).toContainText('725.90');
  await expect(row1).toContainText('381.09');
  await expect(row1).toContainText('762.18');

  // 5. Edit line 2 to R5 values (IM 27.53, uplift 10 => unitCost 309.45, unitPrice 343.83, extPrice 343.83)
  await dialog.getByLabel('Line 2 IM%').fill('27.53');
  await dialog.getByLabel('Line 2 Uplift%').fill('10');

  const row2 = dialog.locator('tbody tr').nth(1);
  await expect(row2).toContainText('309.45');
  await expect(row2).toContainText('343.83');

  // 6. Assert R5 totals: Cost 1,035.35, Price 1,106.01, Margin 70.66 (6.39%)
  await expect(dialog.getByTestId('total-cost')).toHaveText('$1,035.35');
  await expect(dialog.getByTestId('total-price')).toHaveText('$1,106.01');
  await expect(dialog.getByTestId('total-margin')).toHaveText('$70.66 (6.39%)');

  // 7. Submit and inspect request body (linePricing JSON sent, no header margin/imPercent)
  await submitButton.click();

  expect(submittedBody).toContain('name="fxRate"');
  expect(submittedBody).toContain('0.6520');
  expect(submittedBody).toContain('name="linePricing"');
  expect(submittedBody).not.toContain('name="margin"');
  expect(submittedBody).not.toContain('name="imPercent"');

  const jsonMatch = submittedBody.match(/name="linePricing"\r\n\r\n(\[.*?\])\r\n/);
  expect(jsonMatch).not.toBeNull();
  const parsedLinePricing = JSON.parse(jsonMatch![1]);
  expect(parsedLinePricing).toEqual([
    { index: 0, vpn: 'EX4400-48MP', margin: '4.76', imPercent: '15.00' },
    { index: 1, vpn: 'SUB-EX-48-A1-3', margin: '10', imPercent: '27.53' },
  ]);

  // 8. Page 2 shows result, guidance, and Download
  await expect(dialog).toContainText('Parse successful');
  await expect(dialog).toContainText('Administrator guidance for Juniper Iris imports appears here.');

  const downloadPromise = page.waitForEvent('download');
  await dialog.getByRole('button', { name: 'Download' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('Iris_Quote_Synthetic_ForeignUplift.xlsx');
});
