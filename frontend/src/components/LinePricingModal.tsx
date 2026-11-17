import { AlertTriangle, CheckCircle2, Download, Loader2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import type { ParseResponse } from '../api/client';
import type { LinePricingPayloadEntry, ParsePreview, RuntimeVendorDefaults } from '../types';
import {
  computeLine,
  formatDollars2dp,
  formatMoney2dp,
  formatPercent2dp,
  PRICING_INPUT_LIMITS,
  type PricingInputKind,
  acceptsPartialInput,
  isFxRate4dp,
  isImPercent,
  isUplift,
  parseScaled,
  summariseTotals,
} from '../utils/linePricing';
import { ParseResultContent, hasParseResultIssues } from './ParseResultModal';

interface LinePricingModalProps {
  preview: ParsePreview;
  sourceFilename: string;
  vendorDefaults?: RuntimeVendorDefaults;
  guidanceHtml?: string | null;
  detectedFormat?: string | null;
  onCancel: () => void;
  onSubmit: (payload: { fxRate: string; linePricing: LinePricingPayloadEntry[] }) => Promise<ParseResponse>;
  onDownload: (result: ParseResponse) => void;
  onClose: () => void;
}

interface RowEditState {
  imPercent: string;
  imOverridden: boolean;
  uplift: string;
  imEditedAfterApply: boolean;
  upliftEditedAfterApply: boolean;
}

export function LinePricingModal({
  preview,
  sourceFilename,
  vendorDefaults,
  guidanceHtml,
  detectedFormat,
  onCancel,
  onSubmit,
  onDownload,
  onClose,
}: LinePricingModalProps) {
  const [page, setPage] = useState<1 | 2>(1);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [result, setResult] = useState<ParseResponse | null>(null);
  const [inputHint, setInputHint] = useState<string | null>(null);
  const inputHintTimer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(inputHintTimer.current), []);

  /**
   * Applies a typed or pasted value only when it could still become valid, so out-of-range and
   * malformed values (negative, over the limit, too many decimals, letters) never reach the field.
   * A refused change keeps the previous value and briefly explains the limit.
   */
  const guardInput = (kind: PricingInputKind, lineLabel: string | null, raw: string, apply: (value: string) => void) => {
    const value = raw.trim();
    if (acceptsPartialInput(kind, value)) {
      apply(value);
      return;
    }
    window.clearTimeout(inputHintTimer.current);
    setInputHint(lineLabel ? `${lineLabel}: ${PRICING_INPUT_LIMITS[kind]}` : PRICING_INPUT_LIMITS[kind]);
    inputHintTimer.current = window.setTimeout(() => setInputHint(null), 3500);
  };

  const [fxRate, setFxRate] = useState(() => vendorDefaults?.fxRate ?? '');
  const [headerImPercent, setHeaderImPercent] = useState(() => vendorDefaults?.imPercent ?? '');
  const [headerUplift, setHeaderUplift] = useState(() => vendorDefaults?.margin ?? '');

  const [rows, setRows] = useState<RowEditState[]>(() => {
    const defaultIm = vendorDefaults?.imPercent ?? '';
    const defaultUplift = vendorDefaults?.margin ?? '';
    return preview.lines.map((line) => {
      if (preview.imPercentRequired) {
        return {
          imPercent: defaultIm,
          imOverridden: true,
          uplift: defaultUplift,
          imEditedAfterApply: false,
          upliftEditedAfterApply: false,
        };
      }
      return {
        imPercent: line.derivedImPercent ?? '',
        imOverridden: false,
        uplift: defaultUplift,
        imEditedAfterApply: false,
        upliftEditedAfterApply: false,
      };
    });
  });

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && page === 1 && !submitting) {
        onCancel();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [page, submitting, onCancel]);

  const computedRows = useMemo(() => {
    return preview.lines.map((line, idx) => {
      const row = rows[idx];
      const msrpScaled = parseScaled(line.msrp);
      const sourceCostScaled = parseScaled(line.sourceCost);
      const rowImEditable =
        !line.isCancelled && preview.imPercentEditable && msrpScaled !== null && msrpScaled > 0n;
      const computed = computeLine({
        isCancelled: line.isCancelled,
        imPercentRequired: preview.imPercentRequired,
        imPercentEditable: preview.imPercentEditable,
        imOverridden: row.imOverridden,
        qty: line.qty,
        msrpStr: line.msrp,
        sourceCostStr: line.sourceCost,
        imPercentStr: row.imPercent,
        upliftStr: row.uplift,
      });
      return {
        line,
        row,
        rowImEditable,
        msrpScaled,
        sourceCostScaled,
        computed,
      };
    });
  }, [preview.imPercentEditable, preview.imPercentRequired, preview.lines, rows]);

  const totals = useMemo(() => {
    return summariseTotals(
      computedRows.map((r) => ({
        isCancelled: r.line.isCancelled,
        extCost: r.computed.extCost,
        extPrice: r.computed.extPrice,
      })),
    );
  }, [computedRows]);

  const validFxRate = !preview.usesFxRate || isFxRate4dp(fxRate);
  const validHeaderIm = isImPercent(headerImPercent);
  const validHeaderUplift = isUplift(headerUplift);
  const allRowsValid = computedRows.every((r) => r.computed.validIm && r.computed.validUplift);
  const canSubmitPricing = validFxRate && allRowsValid && !submitting;

  const handleApplyImToAll = () => {
    if (!preview.imPercentEditable || !validHeaderIm) return;
    const trimmed = headerImPercent.trim();
    setRows((prev) =>
      prev.map((row, idx) => {
        const line = preview.lines[idx];
        const msrpScaled = parseScaled(line.msrp);
        const rowImEditable =
          !line.isCancelled && preview.imPercentEditable && msrpScaled !== null && msrpScaled > 0n;
        if (!rowImEditable) return row;
        return {
          ...row,
          imPercent: trimmed,
          imOverridden: true,
          imEditedAfterApply: false,
        };
      }),
    );
  };

  const handleApplyUpliftToAll = () => {
    if (!validHeaderUplift) return;
    const trimmed = headerUplift.trim();
    setRows((prev) =>
      prev.map((row, idx) => {
        if (preview.lines[idx].isCancelled) return row;
        return {
          ...row,
          uplift: trimmed,
          upliftEditedAfterApply: false,
        };
      }),
    );
  };

  const handleRowImChange = (index: number, value: string) => {
    setRows((prev) =>
      prev.map((row, idx) => {
        if (idx !== index) return row;
        if (!preview.imPercentRequired && value.trim() === '') {
          return {
            ...row,
            imPercent: '',
            imOverridden: false,
            imEditedAfterApply: false,
          };
        }
        return {
          ...row,
          imPercent: value,
          imOverridden: true,
          imEditedAfterApply: true,
        };
      }),
    );
  };

  const handleRowUpliftChange = (index: number, value: string) => {
    setRows((prev) =>
      prev.map((row, idx) =>
        idx === index
          ? {
              ...row,
              uplift: value,
              upliftEditedAfterApply: true,
            }
          : row,
      ),
    );
  };

  const handleSubmit = async () => {
    if (!canSubmitPricing) return;
    setSubmitting(true);
    setSubmitError(null);
    const linePricing: LinePricingPayloadEntry[] = computedRows.map(({ line, row, rowImEditable }) => ({
      index: line.index,
      vpn: line.vpn,
      margin: line.isCancelled ? '0' : row.uplift.trim(),
      imPercent:
        rowImEditable && (preview.imPercentRequired || row.imOverridden)
          ? row.imPercent.trim()
          : null,
    }));
    try {
      const response = await onSubmit({
        fxRate: fxRate.trim(),
        linePricing,
      });
      setResult(response);
      setPage(2);
    } catch (caught) {
      setSubmitError(caught instanceof Error ? caught.message : 'Could not complete line pricing parse.');
    } finally {
      setSubmitting(false);
    }
  };

  const page2HasIssues = result
    ? hasParseResultIssues({
        validation: result.validation,
        cancelledLines: result.cancelledLines,
        hasRebateIneligibleItems: result.hasRebateIneligibleItems,
      })
    : false;
  const isSplitArchive = (result?.outputWorkbookCount ?? null) !== null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/55 p-4">
      {page === 1 ? (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="line-pricing-title"
          className="flex max-h-[calc(100vh-2rem)] w-auto max-w-[calc(100vw-2rem)] flex-col overflow-hidden rounded-xl border border-slate-200 bg-white shadow-2xl"
        >
          {/* Dialog Header */}
          <div className="flex flex-wrap items-center gap-2.5 border-b border-slate-200 px-5 py-3.5">
            <span id="line-pricing-title" className="text-sm font-bold uppercase tracking-wider text-slate-900">
              Line Pricing Editor
            </span>
            <span className="text-xs text-slate-500">
              {sourceFilename} · {preview.parserDisplayName} · {preview.lines.length}{' '}
              {preview.lines.length === 1 ? 'line' : 'lines'}
            </span>
            <div className="ml-auto flex items-center gap-1.5 text-[11px] font-semibold text-slate-400">
              <span className="flex items-center gap-1.5 text-accent">
                <span className="grid h-[18px] w-[18px] place-items-center rounded-full border border-accent bg-accent text-[10px] text-white">
                  1
                </span>
                Pricing
              </span>
              <span>›</span>
              <span className="flex items-center gap-1.5">
                <span className="grid h-[18px] w-[18px] place-items-center rounded-full border border-slate-300 text-[10px]">
                  2
                </span>
                Result
              </span>
            </div>
          </div>

          {/* Header Bar */}
          <div className="flex flex-wrap items-end gap-3.5 border-b border-slate-200 bg-slate-50 px-5 py-3.5">
            {preview.usesFxRate && (
              <label className="flex flex-col">
                <span className="mb-1 text-[10px] font-bold uppercase tracking-wider text-slate-400">
                  FX (AUD:USD)
                </span>
                <input
                  aria-label="FX (AUD:USD)"
                  aria-invalid={fxRate.trim().length > 0 && !validFxRate}
                  inputMode="decimal"
                  value={fxRate}
                  placeholder="0.6520"
                  onChange={(e) => guardInput('fxRate', null, e.target.value, setFxRate)}
                  className={`h-[34px] w-28 rounded-lg border px-2.5 text-[13px] bg-white focus:outline-none focus:ring-2 ${
                    fxRate.trim().length > 0 && !validFxRate
                      ? 'border-red-400 focus:ring-red-200'
                      : 'border-slate-200 focus:border-accent focus:ring-accent/20'
                  }`}
                />
              </label>
            )}

            {preview.imPercentEditable && (
              <div className="flex flex-col">
                <span className="mb-1 text-center text-[10px] font-bold uppercase tracking-wider text-slate-400">
                  IM% <span className="font-medium normal-case tracking-normal">· Discount off MSRP</span>
                </span>
                <div className="flex items-end gap-1.5">
                  <div className="relative">
                    <input
                      aria-label="IM% · Discount off MSRP"
                      aria-invalid={headerImPercent.trim().length > 0 && !validHeaderIm}
                      inputMode="decimal"
                      value={headerImPercent}
                      placeholder="15.00"
                      onChange={(e) => guardInput('imPercent', null, e.target.value, setHeaderImPercent)}
                      className={`h-[34px] w-24 rounded-lg border pl-2.5 pr-6 text-[13px] bg-white focus:outline-none focus:ring-2 ${
                        headerImPercent.trim().length > 0 && !validHeaderIm
                          ? 'border-red-400 focus:ring-red-200'
                          : 'border-slate-200 focus:border-accent focus:ring-accent/20'
                      }`}
                    />
                    <span className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-xs font-medium text-slate-400">
                      %
                    </span>
                  </div>
                  <button
                    type="button"
                    aria-label="Apply IM% to all"
                    disabled={!validHeaderIm}
                    onClick={handleApplyImToAll}
                    className="h-[34px] rounded-lg border border-slate-200 bg-white px-3.5 text-xs font-semibold text-slate-700 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    Apply to all
                  </button>
                </div>
              </div>
            )}

            <div className="flex flex-col">
              <span className="mb-1 text-center text-[10px] font-bold uppercase tracking-wider text-slate-400">
                Uplift %
              </span>
              <div className="flex items-end gap-1.5">
                <div className="relative">
                  <input
                    aria-label="Uplift %"
                    aria-invalid={headerUplift.trim().length > 0 && !validHeaderUplift}
                    inputMode="decimal"
                    value={headerUplift}
                    placeholder="4.76"
                    onChange={(e) => guardInput('uplift', null, e.target.value, setHeaderUplift)}
                    className={`h-[34px] w-24 rounded-lg border pl-2.5 pr-6 text-[13px] bg-white focus:outline-none focus:ring-2 ${
                      headerUplift.trim().length > 0 && !validHeaderUplift
                        ? 'border-red-400 focus:ring-red-200'
                        : 'border-slate-200 focus:border-accent focus:ring-accent/20'
                    }`}
                  />
                  <span className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 text-xs font-medium text-slate-400">
                    %
                  </span>
                </div>
                <button
                  type="button"
                  aria-label="Apply Uplift to all"
                  disabled={!validHeaderUplift}
                  onClick={handleApplyUpliftToAll}
                  className="h-[34px] rounded-lg border border-slate-200 bg-white px-3.5 text-xs font-semibold text-slate-700 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  Apply to all
                </button>
              </div>
            </div>

            <span className="ml-auto self-center rounded-full bg-sky-50 px-2.5 py-1 text-[11px] font-bold text-accent">
              Bid Currency: {preview.currency || 'USD'}
            </span>
          </div>

          {!preview.validation.matches && preview.validation.warnings.length > 0 && (
            <div className="border-b border-amber-200 bg-amber-50 px-5 py-2 text-xs text-amber-800">
              Source quote validation reported warnings: {preview.validation.warnings.join(' · ')}
            </div>
          )}

          {/* Table */}
          <div className="min-h-0 flex-1 overflow-auto pb-1">
            <table className="w-full border-collapse text-xs tabular-nums">
              <thead>
                <tr>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Line#
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    VPN
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-left text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Description
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Qty
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    MSRP
                  </th>
                  <th
                    className={`sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider whitespace-nowrap ${
                      preview.imPercentEditable ? 'text-accent' : 'text-slate-400'
                    }`}
                  >
                    {preview.imPercentEditable ? 'IM%*' : 'IM%'}
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Cost
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Unit Cost
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Ext Cost
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-accent whitespace-nowrap">
                    Uplift%*
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Unit Price
                  </th>
                  <th className="sticky top-0 z-10 border-b border-slate-200 bg-white px-2.5 py-2.5 text-right text-[10px] font-bold uppercase tracking-wider text-slate-400 whitespace-nowrap">
                    Ext Price
                  </th>
                </tr>
              </thead>
              <tbody>
                {computedRows.map(({ line, row, rowImEditable, msrpScaled, sourceCostScaled, computed }, idx) => {
                  const rowReason = computed.imError ?? computed.upliftError ?? undefined;
                  const unitCostNegative = computed.unitCost !== null && computed.unitCost < 0n;
                  const imHighlight = row.imEditedAfterApply || (!preview.imPercentRequired && row.imOverridden);
                  return (
                    <tr key={line.index} title={rowReason} className="hover:bg-slate-50">
                      <td className="border-b border-slate-100 px-2.5 py-1.5 whitespace-nowrap text-slate-700">
                        {line.lineNumber}
                      </td>
                      <td className="border-b border-slate-100 px-2.5 py-1.5 font-mono text-[11.5px] whitespace-nowrap text-slate-900">
                        {line.vpn}
                      </td>
                      <td
                        title={line.description ?? ''}
                        aria-label={line.description ?? ''}
                        className="max-w-[22rem] truncate border-b border-slate-100 px-2.5 py-1.5 text-slate-600"
                      >
                        <span aria-hidden="true">{line.description ?? ''}</span>
                        <span className="sr-only">{line.description ?? ''}</span>
                      </td>
                      <td className="border-b border-slate-100 px-2.5 py-1.5 text-right whitespace-nowrap text-slate-900">
                        {line.qty}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right whitespace-nowrap ${
                          msrpScaled === null ? 'text-slate-400' : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(msrpScaled)}
                      </td>
                      <td className="border-b border-slate-100 px-2.5 py-1.5 text-right whitespace-nowrap">
                        {rowImEditable ? (
                          <div className="relative inline-flex items-center">
                            <input
                              aria-label={`Line ${line.lineNumber} IM%`}
                              aria-invalid={!computed.validIm}
                              title={computed.imError ?? undefined}
                              inputMode="decimal"
                              value={row.imPercent}
                              placeholder={!preview.imPercentRequired ? (line.derivedImPercent ?? '') : ''}
                              onChange={(e) =>
                                guardInput('imPercent', `Line ${line.lineNumber}`, e.target.value, (value) =>
                                  handleRowImChange(idx, value),
                                )
                              }
                              className={`h-[26px] w-[72px] rounded-md border pl-1.5 pr-4 text-right text-xs focus:outline-none focus:ring-2 ${
                                !computed.validIm
                                  ? 'border-red-400 bg-red-50 ring-1 ring-red-400 focus:ring-red-200'
                                  : imHighlight
                                    ? 'border-amber-400 bg-amber-50 focus:border-accent focus:ring-accent/20'
                                    : 'border-slate-200 bg-white focus:border-accent focus:ring-accent/20'
                              }`}
                            />
                            <span className="pointer-events-none absolute right-1.5 text-[11px] text-slate-400">%</span>
                          </div>
                        ) : (
                          <span className="text-slate-400">
                            {line.derivedImPercent === null ? '—' : `${line.derivedImPercent}%`}
                          </span>
                        )}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right whitespace-nowrap ${
                          sourceCostScaled === null ? 'text-slate-400' : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(sourceCostScaled)}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right font-semibold whitespace-nowrap ${
                          computed.unitCost === null
                            ? 'text-slate-400'
                            : unitCostNegative
                              ? 'text-red-600'
                              : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(computed.unitCost)}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right font-semibold whitespace-nowrap ${
                          computed.extCost === null
                            ? 'text-slate-400'
                            : unitCostNegative
                              ? 'text-red-600'
                              : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(computed.extCost)}
                      </td>
                      <td className="border-b border-slate-100 px-2.5 py-1.5 text-right whitespace-nowrap">
                        {line.isCancelled ? (
                          <span className="text-slate-400">—</span>
                        ) : (
                          <div className="relative inline-flex items-center">
                            <input
                              aria-label={`Line ${line.lineNumber} Uplift%`}
                              aria-invalid={!computed.validUplift}
                              title={computed.upliftError ?? undefined}
                              inputMode="decimal"
                              value={row.uplift}
                              onChange={(e) =>
                                guardInput('uplift', `Line ${line.lineNumber}`, e.target.value, (value) =>
                                  handleRowUpliftChange(idx, value),
                                )
                              }
                              className={`h-[26px] w-[72px] rounded-md border pl-1.5 pr-4 text-right text-xs focus:outline-none focus:ring-2 ${
                                !computed.validUplift
                                  ? 'border-red-400 bg-red-50 ring-1 ring-red-400 focus:ring-red-200'
                                  : row.upliftEditedAfterApply
                                    ? 'border-amber-400 bg-amber-50 focus:border-accent focus:ring-accent/20'
                                    : 'border-slate-200 bg-white focus:border-accent focus:ring-accent/20'
                              }`}
                            />
                            <span className="pointer-events-none absolute right-1.5 text-[11px] text-slate-400">%</span>
                          </div>
                        )}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right font-semibold whitespace-nowrap ${
                          computed.unitPrice === null ? 'text-slate-400' : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(computed.unitPrice)}
                      </td>
                      <td
                        className={`border-b border-slate-100 px-2.5 py-1.5 text-right font-semibold whitespace-nowrap ${
                          computed.extPrice === null ? 'text-slate-400' : 'text-slate-900'
                        }`}
                      >
                        {formatMoney2dp(computed.extPrice)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div role="status" aria-live="polite" className="mx-5">
            {inputHint && (
              <div
                data-testid="input-limit-hint"
                className="mt-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-semibold text-amber-800"
              >
                {inputHint}
              </div>
            )}
          </div>

          {submitError && (
            <div role="alert" className="mx-5 mt-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs font-semibold text-red-700">
              {submitError}
            </div>
          )}

          {/* Footer */}
          <div className="mt-2.5 flex flex-wrap items-center gap-5 border-t border-slate-200 bg-slate-50 px-5 py-3">
            <div>
              <div className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Total cost</div>
              <div data-testid="total-cost" className="mt-0.5 text-xs font-semibold tabular-nums text-slate-900">
                {allRowsValid ? formatDollars2dp(totals.totalCost) : '—'}
              </div>
            </div>
            <div>
              <div className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Total price</div>
              <div data-testid="total-price" className="mt-0.5 text-xs font-semibold tabular-nums text-slate-900">
                {allRowsValid ? formatDollars2dp(totals.totalPrice) : '—'}
              </div>
            </div>
            <div>
              <div className="text-[10px] font-bold uppercase tracking-wider text-slate-400">Total margin</div>
              <div data-testid="total-margin" className="mt-0.5 text-xs font-semibold tabular-nums text-emerald-800">
                {allRowsValid
                  ? `${formatDollars2dp(totals.totalMargin)} (${formatPercent2dp(totals.totalMarginPercent)})`
                  : '—'}
              </div>
            </div>
            {!allRowsValid && (
              <span data-testid="totals-incomplete" className="self-end text-[11px] text-slate-500">
                Complete all lines to see totals
              </span>
            )}

            <div className="ml-auto flex items-center gap-2">
              <button
                type="button"
                disabled={submitting}
                onClick={onCancel}
                className="button px-4"
              >
                Cancel
              </button>
              <button
                type="button"
                disabled={!canSubmitPricing}
                onClick={handleSubmit}
                className="button button-primary px-5"
              >
                {submitting ? (
                  <>
                    <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                    Submitting
                  </>
                ) : (
                  'Submit'
                )}
              </button>
            </div>
          </div>
        </div>
      ) : (
        result && (
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="parse-result-title"
            aria-describedby="parse-result-body"
            className={`w-full max-w-2xl rounded-xl border bg-white shadow-2xl ${
              page2HasIssues ? 'border-amber-300' : 'border-emerald-300'
            }`}
          >
            {/* Header strip */}
            <div
              className={`flex items-center gap-3 rounded-t-xl border-b px-6 py-4 ${
                page2HasIssues ? 'border-amber-200 bg-amber-50' : 'border-emerald-200 bg-emerald-50'
              }`}
            >
              {page2HasIssues ? (
                <AlertTriangle className="h-6 w-6 shrink-0 text-amber-500" aria-hidden="true" />
              ) : (
                <CheckCircle2 className="h-6 w-6 shrink-0 text-emerald-500" aria-hidden="true" />
              )}
              <span
                id="parse-result-title"
                className={`text-base font-bold uppercase tracking-wide ${
                  page2HasIssues ? 'text-amber-800' : 'text-emerald-800'
                }`}
              >
                {page2HasIssues ? 'Parsed with warnings' : 'Parse successful'}
              </span>
              <div className="ml-auto flex items-center gap-1.5 text-[11px] font-semibold text-slate-400">
                <span className="flex items-center gap-1.5 text-slate-600">
                  <span className="grid h-[18px] w-[18px] place-items-center rounded-full border border-emerald-500 bg-emerald-500 text-[10px] text-white">
                    1
                  </span>
                  Pricing
                </span>
                <span>›</span>
                <span className="flex items-center gap-1.5 text-accent">
                  <span className="grid h-[18px] w-[18px] place-items-center rounded-full border border-accent bg-accent text-[10px] text-white">
                    2
                  </span>
                  Result
                </span>
              </div>
            </div>

            <ParseResultContent
              validation={result.validation}
              currency={result.currency}
              computedTotal={result.computedTotal}
              quotedTotal={result.quotedTotal}
              validationWarnings={result.validationWarnings}
              cancelledLines={result.cancelledLines}
              hasRebateIneligibleItems={result.hasRebateIneligibleItems}
              guidanceHtml={guidanceHtml}
              detectedFormat={detectedFormat}
              outputWorkbookCount={result.outputWorkbookCount}
            />

            <div className="flex justify-end gap-3 rounded-b-xl border-t border-slate-200 bg-slate-50 px-6 py-4">
              <button type="button" onClick={onClose} className="button px-6">
                Close
              </button>
              <button
                type="button"
                onClick={() => onDownload(result)}
                className="button button-primary px-6"
                autoFocus
              >
                <Download className="mr-2 h-4 w-4" aria-hidden="true" />
                {isSplitArchive ? 'Download .zip' : 'Download'}
              </button>
            </div>
          </div>
        )
      )}
    </div>
  );
}
