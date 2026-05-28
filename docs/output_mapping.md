**Template:** ANZ-GENERIC_ForeignUplift.xlsx
**Output Name:** `<input_basename>_parsed.xlsx` (e.g. `XQ-4076249.pdf` → `XQ-4076249_parsed.xlsx`)

This file describes how parsed `LineItem` fields are written into the standardised internal template. The template has a single sheet named `Foreign Uplift` with 27 columns A→AA. Every output is a clean copy of the template (no fills, no fonts, no borders, no merged cells) — only values, with date columns carrying a `DD/MM/YYYY` number format.

**Output structure**
- **Row 1**: column L only carries the literal note `(Optional for Software and/or Services)`. All other cells in row 1 are empty.
- **Row 2**: 27 header labels, in the exact order shown below.
- **Rows 3 … N+2**: one line item per row, populated per the field mapping below.
- **Row N+3** (the row immediately after the last line item): the end-loop escape row. Column B = `*`, column D = `DO NOT DELETE THIS LINE. Indicate * on column B to mark the end loop. Add / remove lines above as necessary.` All other cells in this row are empty.

The `*` in column B is required — our quoting system uses it as the loop sentinel when it imports this file.

**Header row (row 2)**

| Col | Header |
|-----|--------|
| A | `Item` |
| B | `Vendor Name` |
| C | `IMTH SKU\n(Optional)` |
| D | `Vendor Part Number` |
| E | `Description` |
| F | `Qty.` |
| G | `Unit Price` |
| H | `MSRP` |
| I | `Cost` |
| J | `Discount` |
| K | `Margin` |
| L | `Product Part Number \n(for Warranty/Renewal)` |
| M | `Serial Number` |
| N | `Warranty / Duration (months)` |
| O | `Vendor Ref` |
| P | `Start Date` |
| Q | `End Date` |
| R | `Comments` |
| S | `Foreign Currency` |
| T | `Foreign Cost` |
| U | `Foreign MSRP` |
| V | `Foreign Exchange Rate` |
| W | `Min Order Qty` |
| X | `IM%` |
| Y | `Diff%` |
| Z | `On Cost %` |
| AA | `Retail Bump %` |

**Field mapping (data rows)**

| Col | Header | Source | Notes |
|-----|--------|--------|-------|
| A | Item | auto-increment from 1 | Row index in the output (1, 2, 3, …) |
| B | Vendor Name | hardcoded `"NUTANIX"` | All formats for now (only Nutanix is supported). |
| C | IMTH SKU | _empty_ | Manual / future enrichment. |
| D | Vendor Part Number | `vpn` | |
| E | Description | `description` | Empty for Renewal when no Platform value is present. For the Platform-column variant (e.g. `XQ-4029825`), hardware rows carry `"Platform: {value}"` here (e.g. `"Platform: NX-8035N-G8-HY"`); software rows remain empty. |
| F | Qty. | `qty` | |
| G | Unit Price | _empty_ | Manual / future enrichment. |
| H | MSRP | _empty_ | **Always blank.** The parser's `msrp` value lands in column U only. |
| I | Cost | _empty_ | Manual / future enrichment. |
| J | Discount | _empty_ | Manual / future enrichment. |
| K | Margin | hardcoded `5.00` | Will become a UI-driven input later. |
| L | Product Part Number (Warranty/Renewal) | _empty_ | Manual / future enrichment. |
| M | Serial Number | _empty_ | **Always blank.** The parser's `serial_number` value lands in column R (Comments) instead. |
| N | Warranty / Duration (months) | `term` | **Only written when `term >= 1`, and only for non-Software-Only formats.** For Software Only (PDF + XLSX), `term` lands in column R instead and N stays empty. If `term` is null or `0`, leave the cell empty. |
| O | Vendor Ref | _empty_ | Manual / future enrichment. |
| P | Start Date | `start_date` | Native Excel date; cell format `DD/MM/YYYY`. Empty if null. |
| Q | End Date | `end_date` | Same as Start Date. |
| R | Comments | `term` (Software Only formats only) **or** `serial_number` (Renewal) | For `nutanix_software_only_pdf` and `nutanix_software_only_xlsx`, written as `"{term} Months"` when `term >= 1` (e.g. `60 Months`). For Renewal, `serial_number` is written verbatim (no `"License: "` prefix, no splitting). The two cases never collide because Renewal items don't carry a `term` and Software Only items don't carry a `serial_number`. Empty otherwise. |
| S | Foreign Currency | hardcoded `"USD"` | All quotes parsed so far are USD-denominated. |
| T | Foreign Cost | `cost` | A value of `0` is written as the sentinel `0.0001` (the downstream uplift app rejects literal `0` and rounds the sentinel back to `0`). See locked rule #2. |
| U | Foreign MSRP | `msrp` | This is where the parser's MSRP value lives. A value of `0` is written as the sentinel `0.0001` (same reason as column T). |
| V | Foreign Exchange Rate | hardcoded `1.000` | Will become a UI-driven input later. |
| W–AA | (other) | _empty_ | Manual / future enrichment. |

**Locked output rules (decided iteratively with the user, in order)**
1. Local `MSRP` (column H) is **never** populated. The parser's `msrp` value lives in `Foreign MSRP` (column U) only.
2. Bundled-component rows (Hardware Only Quote D rows where the supplier left price cells blank) get `msrp = 0` and `cost = 0` at parse time. When writing to `Foreign Cost` (column T) and `Foreign MSRP` (column U), a value of `0` is replaced with the sentinel `0.0001` because the downstream uplift app treats literal `0` as an invalid price — it rounds the sentinel back to `0` on import. The substitution is per-column at write time only; the in-memory `LineItem.Cost` / `LineItem.Msrp` remain `0` and validation totals are unaffected.
3. `Warranty / Duration (months)` (column N) is **only written when `term >= 1`** *and* the parser is not a Software Only format. For Software Only (PDF + XLSX), N stays empty and the term lands in `Comments` (column R) as `"{term} Months"` instead (e.g. `60 Months`). A term of `0` or null is treated as "no term" and both cells are left empty.
4. `Serial Number` column (M) is **never** populated. The supplier's serial-cell string (which may contain an embedded license number, e.g. `"24SW000351227,LIC-02472987"`) is written verbatim into `Comments` (column R) instead. No `"License: "` prefix, no splitting.
5. Numbers are written as raw values — no forced decimal places. Excel will display `383` rather than `383.00` unless a cell format is applied; this is intentional.
6. Dates are written as native `date` values with the cell number format `DD/MM/YYYY` so Excel displays them as DD/MM/YYYY but they remain sortable/filterable as real dates.

**Filename**
The output file is named `<input_basename>_parsed.xlsx` where `<input_basename>` is the source filename without its extension. So:
- `XQ-4076249.pdf` → `XQ-4076249_parsed.xlsx`
- `XQ-4076249.xlsx` → `XQ-4076249_parsed.xlsx` (collides with the PDF in batch reviews; in production only one envelope is processed per parse).

---

## HP (ANZ-GENERIC — No Calculation / Uplift)

**Template:** `ANZ-GENERIC_NoCalculation.xlsx` or `ANZ-GENERIC_Uplift.xlsx` (user-selected at parse time)  
**Writer:** `AnzGenericWriter.Write(items, outputPath, sheetName, includeMargin, margin, vendorName)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (matching the template chosen)

HP writes to the **local** columns of the 27-column layout. No FX rate, no foreign columns.

| Col | Header | HP value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | String: `"1"`, `"2"`, `"1.01"`, `"1.02"`, … |
| B | Vendor Name | `"HP"` | Upper-case vendor label |
| D | Vendor Part Number | `vpn` | `"Product Number/ID"` or `"Product Number/ID#Option Code"` |
| E | Description | `description` | `"Product Description"` from the source row |
| F | Qty. | `qty` | `Max Deal Qty` (Part Number/Bundle) or `Bundle Detail Qty` (Bundle Detail) |
| H | MSRP | **blank** | HP has no MSRP source column |
| I | Cost | `cost` | Part Number / Bundle: `Price` from the source row. Bundle Detail: `0` (component price dropped — the Bundle parent holds the total), exported as the `0.0001` sentinel. |
| K | Margin | `margin` (Uplift only) | Written only when `includeMargin = true`; blank for No Calculation |
| W | Min Order Qty | `min_qty` | After `0 → 1` substitution |

All other columns: blank.

**End-loop sentinel row:** after the last line item — col B = `"*"`, col D = `EndLoopWarning` constant.

**Key differences vs ForeignUplift:**
- Item col A holds the `LineSequence` string (`"1.01"` etc.) rather than a running integer
- Zero costs export as the `0.0001` sentinel in column I (every Bundle Detail, whose price is dropped onto its Bundle parent); non-zero Part Number / Bundle costs are written as-is
- No term, date, serial number, or FX columns populated
- `Matches = true` always (HP files have no quoted total to compare against)

---

## HP (ANZ-GENERIC — % Off RRP with Uplift)

**Template:** `ANZ-GENERIC_PercentOffWithUplift.xlsx`
**Writer:** `PercentOffWithUpliftWriter.Write(items, outputPath, margin, imPercent, vendorName)`
**Sheet name:** `"% Off RRP with Uplift"`

Used by **HP OneConfig (XLSX)** only. Unlike the No Calculation / Uplift writers, this template puts all pricing on the **MSRP** column (H) — column I (Cost) is intentionally blank. Margin (K) and IM% (X) are both required and always written.

| Col | Header | OneConfig value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"` (parent), `"1.01"`, `"1.02"`, … (children) |
| B | Vendor Name | `"HP"` | Upper-case vendor label |
| D | Vendor Part Number | `vpn` | Parent: `Config ID`; children: `Part Number` |
| E | Description | `description` | Parent: `Config Name`; children: source `Description` |
| F | Qty. | `qty` | Parent: always 1; children: source `Quantity` |
| H | MSRP | parent: `msrp`; children: `0.0001` sentinel | Parent carries the real `Total Price`; children's source prices are intentionally dropped |
| I | Cost | **blank** | Cost is unused for this template |
| K | Margin | `margin` | User-supplied; always written |
| X | IM% | `imPercent` | User-supplied; always written; required (parse fails with 400 if omitted) |

All other columns: blank.

**End-loop sentinel row:** after the last line item — col B = `"*"`, col D = `EndLoopWarning` constant (shared with the other writers).

**Key differences vs the other HP templates:**
- Pricing lands on column H (MSRP), not column I (Cost)
- Both `margin` and `im_percent` are mandatory parse parameters
- `User.ImPercent` (`im` DB column) is persisted as a per-user default, serialised as `im_percent` in JSON
