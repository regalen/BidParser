const SCALE = 1_000_000_000_000n; // 10^12
const HUNDRED_SCALED = 100n * SCALE; // 100 * 10^12
const CENT_DIVISOR = 10_000_000_000n; // 10^10 (since 10^12 / 10^10 = 10^2 cents)
const HALF_CENT = 5_000_000_000n;

export function parseScaled(value: string | number | null | undefined): bigint | null {
  if (value == null) return null;
  const trimmed = String(value).trim();
  if (!trimmed) return null;
  const match = /^(-?)(\d+)(?:\.(\d+))?$/.exec(trimmed);
  if (!match) return null;
  const sign = match[1] === '-' ? -1n : 1n;
  const intPart = BigInt(match[2]);
  const fracStr = (match[3] ?? '').padEnd(12, '0').slice(0, 12);
  const fracPart = BigInt(fracStr || '0');
  return sign * (intPart * SCALE + fracPart);
}

/**
 * Half-away-from-zero to 2 decimal places, returning a 10^12-scaled bigint
 * that is an exact multiple of 10^10.
 */
export function round2Scaled(v: bigint): bigint {
  if (v >= 0n) {
    return ((v + HALF_CENT) / CENT_DIVISOR) * CENT_DIVISOR;
  }
  return -(((-v + HALF_CENT) / CENT_DIVISOR) * CENT_DIVISOR);
}

/**
 * Divide numerator by denominator into a 10^12-scaled bigint (half-away-from-zero at 10^-12),
 * where numerator and denominator are already scaled consistently (ratio * SCALE).
 */
function divScaled(numerator: bigint, denominator: bigint): bigint {
  if (denominator === 0n) return 0n;
  const negative = (numerator < 0n) !== (denominator < 0n);
  const absNum = numerator < 0n ? -numerator : numerator;
  const absDen = denominator < 0n ? -denominator : denominator;
  const scaledNum = absNum * SCALE;
  const quotient = (scaledNum + absDen / 2n) / absDen;
  return negative ? -quotient : quotient;
}

function mulScaled(a: bigint, b: bigint): bigint {
  const negative = (a < 0n) !== (b < 0n);
  const absA = a < 0n ? -a : a;
  const absB = b < 0n ? -b : b;
  const prod = (absA * absB + SCALE / 2n) / SCALE;
  return negative ? -prod : prod;
}

export function formatMoney2dp(v: bigint | null): string {
  if (v === null) return '—';
  const rounded = round2Scaled(v);
  const cents = rounded / CENT_DIVISOR;
  const negative = cents < 0n;
  const absCents = negative ? -cents : cents;
  const whole = absCents / 100n;
  const frac = (absCents % 100n).toString().padStart(2, '0');
  const wholeFormatted = whole.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${negative ? '-' : ''}${wholeFormatted}.${frac}`;
}

/** Money with a leading dollar sign, e.g. "$1,035.35" or "-$12.50"; "—" when absent. */
export function formatDollars2dp(v: bigint | null): string {
  if (v === null) return '—';
  const money = formatMoney2dp(v);
  return money.startsWith('-') ? `-$${money.slice(1)}` : `$${money}`;
}

export function formatPercent2dp(v: bigint | null): string {
  if (v === null) return '—';
  const rounded = round2Scaled(v);
  const cents = rounded / CENT_DIVISOR;
  const negative = cents < 0n;
  const absCents = negative ? -cents : cents;
  const whole = absCents / 100n;
  const frac = (absCents % 100n).toString().padStart(2, '0');
  return `${negative ? '-' : ''}${whole.toString()}.${frac}%`;
}

/**
 * IM%: 0 <= IM% <= 100, at most 2 dp. 100 makes the line free (written as the zero-price sentinel).
 */
export function isImPercent(value: string): boolean {
  const trimmed = value.trim();
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return false;
  const scaled = parseScaled(trimmed);
  return scaled !== null && scaled >= 0n && scaled <= HUNDRED_SCALED;
}

export type PricingInputKind = 'imPercent' | 'uplift' | 'fxRate';

/** Shown when a keystroke or paste is refused because the result could never be valid. */
export const PRICING_INPUT_LIMITS: Record<PricingInputKind, string> = {
  imPercent: 'IM% must be between 0 and 100, with up to 2 decimal places.',
  uplift: 'Uplift must be at least 0 and below 100, with up to 2 decimal places.',
  fxRate: 'FX must be a positive number with up to 4 decimal places.',
};

// Partial-entry shapes: digits with at most one decimal point and the field's decimal places.
// No sign is possible, so negative values cannot be typed. Uplift allows two integer digits,
// which caps it below 100; IM% allows three and is range-checked below.
const PARTIAL_INPUT: Record<PricingInputKind, RegExp> = {
  imPercent: /^\d{0,3}(\.\d{0,2})?$/,
  uplift: /^\d{0,2}(\.\d{0,2})?$/,
  fxRate: /^\d{0,6}(\.\d{0,4})?$/,
};

/**
 * Whether `value` may stay in the field while the user is typing: empty and part-typed values
 * such as "12." are allowed, values that can never become valid are not.
 */
export function acceptsPartialInput(kind: PricingInputKind, value: string): boolean {
  if (value === '') return true;
  if (!PARTIAL_INPUT[kind].test(value)) return false;
  if (kind === 'imPercent') {
    const digits = value.endsWith('.') ? value.slice(0, -1) : value;
    const scaled = parseScaled(digits === '' ? '0' : digits);
    return scaled !== null && scaled <= HUNDRED_SCALED;
  }
  return true;
}

/**
 * Uplift: 0 <= m < 100, at most 2 dp.
 */
export function isUplift(value: string): boolean {
  const trimmed = value.trim();
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return false;
  const scaled = parseScaled(trimmed);
  return scaled !== null && scaled >= 0n && scaled < HUNDRED_SCALED;
}

/**
 * Exchange rate: > 0, at most 4 dp.
 */
export function isFxRate4dp(value: string): boolean {
  const trimmed = value.trim();
  if (!/^\d+(\.\d{1,4})?$/.test(trimmed)) return false;
  const scaled = parseScaled(trimmed);
  return scaled !== null && scaled > 0n;
}

/**
 * Formula: CostFromMsrp(msrp, imPct) = Round2(msrp * (1 - imPct / 100))
 */
export function costFromMsrp(msrp: bigint, imPct: bigint): bigint {
  const factor = HUNDRED_SCALED - imPct; // (100 - imPct) * 10^12
  const raw = mulScaled(msrp, factor) / 100n;
  return round2Scaled(raw);
}

/**
 * Formula: DerivedImPercent(sourceCost, msrp) = msrp > 0 ? Round2((1 - sourceCost / msrp) * 100) : null
 */
export function derivedImPercent(sourceCost: bigint | null, msrp: bigint | null): bigint | null {
  if (msrp === null || msrp <= 0n || sourceCost === null) return null;
  const diff = (msrp - sourceCost) * 100n;
  const ratio = divScaled(diff, msrp);
  return round2Scaled(ratio);
}

/**
 * Formula: EffectiveUnitCost
 * - If imPercentRequired (CalculatesCostFromMsrp): CostFromMsrp(msrp, imPct)
 * - Otherwise, if imOverridden and msrp > 0: CostFromMsrp(msrp, imPct)
 * - Otherwise: unrounded sourceCost
 */
export function effectiveUnitCost(
  imPercentRequired: boolean,
  imOverridden: boolean,
  msrp: bigint | null,
  sourceCost: bigint | null,
  imPct: bigint | null,
): bigint {
  if (imPercentRequired) {
    return costFromMsrp(msrp ?? 0n, imPct ?? 0n);
  }
  if (imOverridden && msrp !== null && msrp > 0n && imPct !== null) {
    return costFromMsrp(msrp, imPct);
  }
  return sourceCost ?? 0n;
}

/**
 * Formula: ExtCost(unitCost, qty) = Round2(unitCost * qty)
 */
export function extCost(unitCost: bigint, qty: number): bigint {
  return round2Scaled(unitCost * BigInt(qty));
}

/**
 * Formula: UnitPrice(unitCost, upliftPct) = Round2(unitCost / (1 - upliftPct / 100))
 */
export function unitPrice(unitCost: bigint, upliftPct: bigint): bigint {
  const denom = HUNDRED_SCALED - upliftPct; // (100 - upliftPct) * 10^12
  if (denom <= 0n) return 0n;
  const raw = divScaled(unitCost * 100n, denom);
  return round2Scaled(raw);
}

/**
 * Formula: ExtPrice(unitPrice, qty) = Round2(unitPrice * qty)
 */
export function extPrice(uPrice: bigint, qty: number): bigint {
  return round2Scaled(uPrice * BigInt(qty));
}

export interface ComputedLine {
  validIm: boolean;
  validUplift: boolean;
  imError: string | null;
  upliftError: string | null;
  unitCost: bigint | null;
  extCost: bigint | null;
  unitPrice: bigint | null;
  extPrice: bigint | null;
}

export function computeLine(params: {
  isCancelled: boolean;
  imPercentRequired: boolean;
  imPercentEditable: boolean;
  imOverridden: boolean;
  qty: number;
  msrpStr: string | null;
  sourceCostStr: string | null;
  imPercentStr: string;
  upliftStr: string;
}): ComputedLine {
  if (params.isCancelled) {
    return {
      validIm: true,
      validUplift: true,
      imError: null,
      upliftError: null,
      unitCost: null,
      extCost: null,
      unitPrice: null,
      extPrice: null,
    };
  }

  const msrp = parseScaled(params.msrpStr);
  const sourceCost = parseScaled(params.sourceCostStr);
  const rowImEditable = params.imPercentEditable && msrp !== null && msrp > 0n;

  let validIm = true;
  let imError: string | null = null;
  if (rowImEditable) {
    if (params.imPercentRequired) {
      validIm = isImPercent(params.imPercentStr);
      if (!validIm) {
        imError = 'IM% is required and must be between 0 and 100 with at most 2 decimal places.';
      }
    } else if (params.imOverridden) {
      validIm = isImPercent(params.imPercentStr);
      if (!validIm) {
        imError = 'IM% must be between 0 and 100 with at most 2 decimal places.';
      }
    }
  }

  const validUplift = isUplift(params.upliftStr);
  const upliftError = validUplift
    ? null
    : 'Uplift is required and must be at least 0 and less than 100 with at most 2 decimal places.';

  if (!validIm) {
    return {
      validIm: false,
      validUplift,
      imError,
      upliftError,
      unitCost: null,
      extCost: null,
      unitPrice: null,
      extPrice: null,
    };
  }

  const imScaled =
    rowImEditable && (params.imPercentRequired || params.imOverridden)
      ? (parseScaled(params.imPercentStr) ?? 0n)
      : null;

  const uCost = effectiveUnitCost(
    params.imPercentRequired,
    params.imOverridden,
    msrp,
    sourceCost,
    imScaled,
  );
  const eCost = extCost(uCost, params.qty);

  if (!validUplift) {
    return {
      validIm: true,
      validUplift: false,
      imError: null,
      upliftError,
      unitCost: uCost,
      extCost: eCost,
      unitPrice: null,
      extPrice: null,
    };
  }

  const upliftScaled = parseScaled(params.upliftStr) ?? 0n;
  const uPrice = unitPrice(uCost, upliftScaled);
  const ePrice = extPrice(uPrice, params.qty);

  return {
    validIm: true,
    validUplift: true,
    imError: null,
    upliftError: null,
    unitCost: uCost,
    extCost: eCost,
    unitPrice: uPrice,
    extPrice: ePrice,
  };
}

export interface TotalsSummary {
  totalCost: bigint;
  totalPrice: bigint;
  totalMargin: bigint;
  totalMarginPercent: bigint | null;
}

export function summariseTotals(
  lines: ReadonlyArray<{ isCancelled?: boolean; extCost: bigint | null; extPrice: bigint | null }>,
): TotalsSummary {
  let sumCost = 0n;
  let sumPrice = 0n;
  for (const line of lines) {
    if (line.isCancelled) continue;
    if (line.extCost !== null) sumCost += line.extCost;
    if (line.extPrice !== null) sumPrice += line.extPrice;
  }
  const totalCost = round2Scaled(sumCost);
  const totalPrice = round2Scaled(sumPrice);
  const totalMargin = round2Scaled(totalPrice - totalCost);
  const totalMarginPercent =
    totalPrice <= 0n ? null : round2Scaled(divScaled(totalMargin * 100n, totalPrice));
  return { totalCost, totalPrice, totalMargin, totalMarginPercent };
}
