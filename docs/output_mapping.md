**Template:** `ANZ-GENERIC_*.xlsx`
**Output Name:** `<BidNumber>_<BidRevision>_<FileToken>.xlsx` when both bid fields are available; otherwise `<input_basename>_<FileToken>.xlsx` (e.g. bid `XQ-9100002`, revision `1`, and `Foreign Uplift` → `XQ-9100002_1_ForeignUplift.xlsx`)

This file describes how parsed `LineItem` fields are written into the standardised internal template. The template has a single sheet named per the CRM template (e.g. `Foreign Uplift`, `No Calculation`, `Uplift`, `% Off RRP with Uplift`) with 27 columns A→AA. Every output is a clean copy of the layout (no fills, no fonts, no borders, no merged cells) — only values, with date columns carrying a `DD/MM/YYYY` number format.

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

## Foreign Uplift (Nutanix) — field mapping

The mapping below is for the **`Foreign Uplift`** template, which today is used by the six Nutanix
formats only. Every other vendor writes a local-currency template; those mappings are in the
per-vendor sections further down.

`CrmWriter` is the single writer for all templates and is format-agnostic: the per-column behaviour
comes from `CrmTemplateProfile`, and the values below marked as supplied by `options` come from the
`CrmWriterOptions` that `ParseService` builds from the parser and the user's parse-time inputs.

| Col | Header | Source | Notes |
|-----|--------|--------|-------|
| A | Item | auto-increment from 1 | Row index in the output (1, 2, 3, …). Nutanix parsers set no `LineSequence`, so this template's numeric fallback applies. |
| B | Vendor Name | `options.VendorName` → `"NUTANIX"` | Supplied by `ParseService` from `parser.OutputVendorName.ToUpperInvariant()`. Not hardcoded — every vendor uses the same writer. |
| C | IMTH SKU | _empty_ | Manual / future enrichment. |
| D | Vendor Part Number | `vpn` | Nutanix parsers normalize the value to uppercase for SAP SKU resolution; source casing remains in `Raw`. |
| E | Description | `description` | Empty for Renewal when no Platform value is present. For the Platform-column variant (e.g. `XQ-9100001`), hardware rows carry `"Platform: {value}"` here (e.g. `"Platform: NX-8035N-G8-HY"`); software rows remain empty. |
| F | Qty. | `qty` | |
| G | Unit Price | _empty_ | Manual / future enrichment. |
| H | MSRP | _empty_ | **Always blank.** The parser's `msrp` value lands in column U only. |
| I | Cost | _empty_ | Manual / future enrichment. |
| J | Discount | _empty_ | Manual / future enrichment. |
| K | Margin | `margin` | User-supplied at parse time (rendered as **Uplift** in the UI). `CrmWriterOptions` carries a `5.00` default, but `ParseService` always passes the user's value. |
| L | Product Part Number (Warranty/Renewal) | _empty_ | Manual / future enrichment. |
| M | Serial Number | `serial_number` | Written verbatim when populated; embedded license values remain comma-joined with no prefix or splitting. |
| N | Warranty / Duration (months) | `term` | **Only written when `term >= 1`, and only for non-Software-Only formats.** For Software Only (PDF + XLSX), `term` lands in column R instead and N stays empty. If `term` is null or `0`, leave the cell empty. |
| O | Vendor Ref | _empty_ | Manual / future enrichment. |
| P | Start Date | `start_date` | Native Excel date; cell format `DD/MM/YYYY`. Empty if null. |
| Q | End Date | `end_date` | Same as Start Date. |
| R | Comments | `comments` **or** `term` (Software Only formats only) | Writes `comments` when populated. For `nutanix_software_only_pdf` and `nutanix_software_only_xlsx`, `term` is written as `"{term} Months"` when `term >= 1` (e.g. `60 Months`). Empty otherwise. The Software Only formats select term placement by declaring `TermRendersAsComment => true` on `IParser`; `CrmWriter` reads that flag and never inspects the vendor or slug. |
| S | Foreign Currency | `QuoteMetadata.Currency` | Comes from the parsed quote, not a constant. `"USD"` for every Nutanix quote to date. |
| T | Foreign Cost | `cost` | A value of `0` is written as the sentinel `0.0001` (the downstream uplift app rejects literal `0` and rounds the sentinel back to `0`). See locked rule #2. |
| U | Foreign MSRP | `msrp` | This is where the parser's MSRP value lives. A value of `0` is written as the sentinel `0.0001` (same reason as column T). |
| V | Foreign Exchange Rate | `fx_rate` | User-supplied at parse time. `CrmWriterOptions` carries a `1.000` default, but `ParseService` always passes the user's value. |
| W–AA | (other) | _empty_ | Manual / future enrichment. |

**Locked output rules** (rules 1 and 3 are specific to `Foreign Uplift`; 2 and 4–6 apply to every template)
1. On `Foreign Uplift`, local `MSRP` (column H) is **never** populated — the parser's `msrp` value lives in `Foreign MSRP` (column U) only. Other templates do populate column H; see the per-vendor sections.
2. **The two zero prices.** CRM does not accept a literal `0` as a price, which gives the output two deliberate ways to write zero. They mean opposite things — see "Zero pricing: the two zeros" below for the full rule. Bundled-component rows (Hardware Only Quote D rows where the supplier left price cells blank) get `msrp = 0` and `cost = 0` at parse time and are a **`ZeroPriceSentinel`** case: when writing to `Foreign Cost` (column T) and `Foreign MSRP` (column U), a value of `0` is replaced with `0.0001`, which CRM rounds back to `0` on import so the line lands at $0.00. The substitution is per-column at write time only; the in-memory `LineItem.Cost` / `LineItem.Msrp` remain `0` and validation totals are unaffected.
3. `Warranty / Duration (months)` (column N) is **only written when `term >= 1`** *and* the parser does not declare `TermRendersAsComment` (the two Software Only formats do). For Software Only (PDF + XLSX), N stays empty and the term lands in `Comments` (column R) as `"{term} Months"` instead (e.g. `60 Months`). A term of `0` or null is treated as "no term" and both cells are left empty.
4. `Serial Number` column (M) is populated from `serial_number`. The supplier's serial-cell string may contain an embedded license number (e.g. `"24SW000351227,LIC-02472987"`) and is written verbatim with no `"License: "` prefix or splitting. `Comments` remains independent in column R, so both fields can be emitted on the same line.
5. Numbers are written as raw values — no forced decimal places. Excel will display `383` rather than `383.00` unless a cell format is applied; this is intentional.
6. Dates are written as native `date` values with the cell number format `DD/MM/YYYY` so Excel displays them as DD/MM/YYYY but they remain sortable/filterable as real dates.

**Filename**
When both parsed bid fields are available, the output file is named
`<BidNumber>_<BidRevision>_<FileToken>.xlsx` (for parsers using the default `OutputNameStyle.BidScoped`). `<FileToken>` comes from
`CrmTemplates.FileToken(crmTemplate)`. Bid fields are sanitised to `[A-Za-z0-9._-]` before they
enter a filename. So:
- bid `XQ-9100002`, revision `1`, and `Foreign Uplift` → `XQ-9100002_1_ForeignUplift.xlsx`
- bid `BRDAD011030915`, revision `3`, and `Uplift` → `BRDAD011030915_3_Uplift.xlsx`

For parsers declaring `OutputNameStyle.SolutionScoped` (e.g. `hp_services_xlsx`), whole-quote downloads and ZIP archives are named `<BidNumber>_<FileToken>.xlsx` and `<BidNumber>_<FileToken>.zip` without a revision suffix (e.g. `CH9000000002_NoCalculation.xlsx` / `CH9000000002_NoCalculation.zip`), and split workbooks inside the archive are named `<solutionToken>_<FileToken>.xlsx` (e.g. `1073_3013_5309_NoCalculation.xlsx`), where whitespace runs in the solution ID are converted to underscores `_`.

If either `BidNumber` or `BidRevision` is unavailable (including historical jobs created without
bid metadata), naming falls back to the prior convention: `<input_basename>_<FileToken>.xlsx`,
where `<input_basename>` is the source filename without its extension.

For a Solution-ID split under `BidScoped`, the retained/downloaded output is an archive named
`<BidNumber>_<BidRevision>_<FileToken>.zip`. Its workbook entries are named
`<BidNumber>_<BidRevision>_<solutionId>_<FileToken>.xlsx`; lines without an ID use the token
`NoSolutionID`. The same all-or-nothing source-basename fallback applies when bid metadata is
unavailable. Solution IDs are sanitised to `[A-Za-z0-9._-]` before they enter a filename or ZIP
entry name.

---

## Zero pricing: the two zeros

**CRM does not accept a literal `0` as a price.** That single fact gives the output two
deliberate ways to write zero into a price column. They mean opposite things, and picking the
wrong one silently changes what the customer is quoted — so every writer states which it means.
Both constants live in `TemplateLayout`; **never write a bare `0` to a price column.**

| | `TemplateLayout.ZeroPriceSentinel` | `TemplateLayout.NoBidPrice` |
|---|---|---|
| **Value written** | `0.0001` | `0` (literal) |
| **Means** | "This line really is free." | "This line carries no bid price." |
| **What CRM does** | Accepts it, rounds back to `0.00` on import | Rejects it as a price, falls back to the line's **standard price held in SAP** |
| **Customer sees** | $0.00 | The SAP standard price |
| **Used for** | Dropped-component lines whose price sits on their parent: HP `Bundle Detail`, HPE `BundleDetails`, Lenovo / Cisco / Dell CTO children, HP OneConfig children | Lines that belong on the quote but that we deliberately do not bid-price: today only Zebra `Cancelled = Y` rows |
| **How to apply** | `TemplateLayout.NonZeroPrice(value)` — never by hand | `TemplateLayout.NoBidPrice`, explicitly |

The sentinel is the **default** for a zero price. `NoBidPrice` is the narrow exception and needs a
documented business reason per format — it is never a stand-in for a genuine $0.00, and a genuine
$0.00 must never be written as a bare `0`.

Both are **write-time only**: the in-memory `LineItem.Cost` / `LineItem.Msrp` keep their parsed
values, so validation totals (`computed_total = Σ(cost × qty)`) are unaffected by either.

---

## CRM Upload Templates Column Visibility

All 8 CRM upload template workbooks are checked in under `samples/template/` and share the exact
same 27-column layout (A→AA) and shared strings. They differ only in sheet tab name, default
formatting, and column visibility.

**Only four are wired up.** A template is selectable only when `CrmTemplateProfile.For` returns a
profile for it — today `Foreign Uplift`, `No Calculation`, `Uplift`, and `% Off RRP with Uplift`.
The other four workbooks in the table below (`RRPDiscount`, `PercentOffWithDiff`,
`ForeignPercentOffWithUplift`, `ForeignPercentOffWithDiff`) are reference copies of CRM's other
upload layouts, retained for when a future format needs one; adding one means adding a
`CrmTemplateProfile` entry and a `CrmTemplates` constant, not a new writer.

| Template | Columns visible beyond common A–F, L–R, W, Z, AA |
|---|---|
| `NoCalculation` | G Unit Price, H MSRP, I Cost, S, T, U |
| `Uplift` | H MSRP, I Cost, K Margin |
| `RRPDiscount` | H, I, J Discount, K Margin |
| `PercentOffWithUplift` | H, I, K Margin, X IM% |
| `PercentOffWithDiff` | H, X IM%, Y Diff% (no Cost, no Margin) |
| `ForeignUplift` | H, K Margin, S/T/U/V foreign block |
| `ForeignPercentOffWithUplift` | K Margin, S, U, V, X IM% (no Foreign Cost) |
| `ForeignPercentOffWithDiff` | S, U, V, X IM%, Y Diff% (no Margin, no Foreign Cost) |

---

## HP (ANZ-GENERIC — No Calculation / Uplift)

**Template:** `ANZ-GENERIC_NoCalculation.xlsx` or `ANZ-GENERIC_Uplift.xlsx` (user-selected at parse time)  
**Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (matching the template chosen)

HP writes to the **local** columns of the 27-column layout. No FX rate, no foreign columns.

| Col | Header | HP value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | String: `"1"`, `"2"`, `"1.01"`, `"1.02"`, … |
| B | Vendor Name | `"HP"` | Upper-case vendor label |
| D | Vendor Part Number | `vpn` | `"Product Number/ID"` or `"Product Number/ID#Option Code"` |
| E | Description | `description` | `"Product Description"` from the source row |
| F | Qty. | `qty` | `Min Order Qty` after `0 → 1` (Part Number/Bundle) or `Bundle Detail Qty` (Bundle Detail) |
| H | MSRP | **blank** | HP has no MSRP source column |
| I | Cost | `cost` | Part Number / Bundle: `Price` from the source row. Bundle Detail: `0` (component price dropped — the Bundle parent holds the total), exported as the `0.0001` sentinel. |
| K | Margin | `margin` (Uplift only) | Written only on the `Uplift` profile; blank for No Calculation |
| R | Comments | `comments` | Part Number / Bundle: `"Max Qty: {Max Deal Qty}"` (blank when `Max Deal Qty` is absent). Bundle Detail: blank (no `Max Deal Qty`). |
| W | Min Order Qty | `min_qty` | After `0 → 1` substitution (same value as Qty for Part Number/Bundle) |

All other columns: blank.

**End-loop sentinel row:** after the last line item — col B = `"*"`, col D = `EndLoopWarning` constant.

**Key differences vs ForeignUplift:**
- Item col A holds the `LineSequence` string (`"1.01"` etc.) rather than a running integer
- Zero costs export as the `0.0001` sentinel in column I (every Bundle Detail, whose price is dropped onto its Bundle parent); non-zero Part Number / Bundle costs are written as-is
- Comments col R holds `"Max Qty: {Max Deal Qty}"` for Part Number / Bundle lines (the deal quantity, not used as Qty); blank for Bundle Detail
- No term, date, serial number, or FX columns populated
- `Matches = true` always (HP files have no quoted total to compare against)

---

## HPE (ANZ-GENERIC — No Calculation / Uplift)

**Parser:** `HpeBidXlsxParser` (`hpe_bid_xlsx`)  
**Template / Writer:** same as HP Bid above — `CrmWriter.Write(..., vendorName: "HPE")`  
**Source anchor:** `"LineType"` (one word); metadata block is key (col A) / value (col B)

Same 27-column ANZ-GENERIC layout. Differences from HP Bid:

| Col | Header | HPE value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, `"1.01"`, … (Bundle opens a child group; `BundleDetails` nest as `parent.NN`) |
| B | Vendor Name | `"HPE"` | |
| D | Vendor Part Number | `vpn` | `ProductNumber` (Part Number) / `BundleID` (Bundle) / `ComponentID` plus `"#{OptionCode}"` when non-empty (BundleDetails only) |
| E | Description | `description` | `ProductDescription` |
| F | Qty. | `qty` | From the **`Quantity`** column (`0 → 1`) — *not* Min Order Qty as in HP Bid |
| H | MSRP | `msrp` | **Populated** from `ListPrcEst` (Part Number/Bundle); `0` → `0.0001` sentinel. BundleDetails = sentinel. |
| I | Cost | `cost` | `Offering` (Part Number/Bundle); `0` → `0.0001` sentinel. BundleDetails = sentinel (component price dropped onto the Bundle parent). |
| K | Margin | `margin` (Uplift only) | Written only on the `Uplift` profile |
| R | Comments | **blank** | `MaxDealQty` is not output |
| W | Min Order Qty | **blank** | `MinOrderQty` is not output |

**Key differences vs HP Bid:**
- MSRP (col H) is populated from `ListPrcEst` — HP Bid leaves col H blank. The `0 → 0.0001`
  sentinel now also applies to MSRP (via `CrmWriter`/`TemplateLayout.NonZeroPrice`).
- Qty comes only from `Quantity`; Min Order Qty and Comments remain blank.
- `Matches = true` always (no quoted total). See `docs/hpe_bid_xlsx.md`.

---

## HP Global Bid (ANZ-GENERIC — No Calculation / Uplift)

**Parser:** `HpGlobalBidXlsxParser` (`hp_global_bid_xlsx`)  
**Template / Writer:** same as HP Bid above — `CrmWriter.Write`  
**Source sheet:** `"Product numbers"` (by name), header row anchored on `"Product number"`

Same 27-column layout. Differences from HP Bid:

| Col | Header | Global Bid value | Notes |
|---|---|---|---|
| A | Item | auto-increment 1, 2, 3… | Writer fallback counter (LineSequence not set) |
| B | Vendor Name | `"HP"` | |
| D | Vendor Part Number | `vpn` | Source col B `"Product number"` |
| E | Description | `description` | Source col E `"Description"` |
| F | Qty. | `qty` | Always `1` (`"Aggregated item quantity"` is not read) |
| I | Cost | `cost` | Source col G `"Converted net price [AUD]"` (AUD prefix stripped by `DecimalCleaner`) |
| K | Margin | `margin` (Uplift only) | |
| R | Comments | `comments` | `"{remaining} Remaining"`. `remaining` = `"Remaining qty"`. (`"Full term (Months)"` is not parsed.) |

**AUD validation:** parser throws `ParseError` (category `"currency"`) if the header row does not contain `"Converted net price [AUD]"` — prevents non-AUD quotes from being silently mis-parsed.

**Quote number:** extracted from the `"About this deal"` sheet — cell adjacent to `"Deal Number"` label. Falls back to filename without extension.

---

## HP (ANZ-GENERIC — % Off RRP with Uplift)

**Template:** `ANZ-GENERIC_PercentOffWithUplift.xlsx`
**Writer:** `CrmWriter.Write(items, outputPath, CrmTemplates.PercentOffWithUplift, options)`
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
- Both `margin` and `imPercent` are mandatory parse parameters
- `User.ImPercent` (`im` DB column) is persisted as a per-user default, serialised as `imPercent` in JSON

---

## HP Services (ANZ-GENERIC — No Calculation / Uplift)

**Parser:** `HpServicesXlsxParser` (`hp_services_xlsx`)  
**Template / Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (user-selected)  
**Source anchors:** Header row anchored on `"Contract ID"`.  

Uses the 27-column ANZ-GENERIC layout. Populates serial numbers, coverage start/end dates, and supports SAID splitting.

| Col | Header | HP Services value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | String `"1"`, `"2"`, `"3"`, … (flat sequential counter) |
| B | Vendor Name | `"HP"` | `options.VendorName` (`"HP"`) |
| D | Vendor Part Number | `vpn` | `Service Product Number` (e.g. `HA151AC`, `UJ561AC`) |
| E | Description | `description` | Composed: `{Service Product Description} - {Product Number} {Product Description} (Warranty End Date: dd/MM/yyyy)` |
| F | Qty. | `qty` | `Quantity` (default 1 if 0/blank) |
| H | MSRP | **blank** | No MSRP source column |
| I | Cost | `cost` | `Line Item Total Net Price` |
| K | Margin | `margin` (Uplift only) | User-supplied margin % |
| M | Serial Number | `serial_number` | `Serial Number` |
| P | Start Date | `start_date` | `Coverage Start` (`DD/MM/YYYY`) |
| Q | End Date | `end_date` | `Coverage End` (`DD/MM/YYYY`) |

All other columns: blank. End-loop sentinel row after the last line item: col B = `"*"`, col D = `EndLoopWarning` constant.

---

## Lenovo LBP-E ISG Quote (ANZ-GENERIC — No Calculation / Uplift)

**Parser:** `LenovoLbpeIsgXlsParser` (`lenovo_lbpe_isg_xls`)  
**Template / Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (user-selected)  
**Source anchors:** Legacy OLE `.xls` / OpenXML `.xlsx` via ExcelDataReader; the row containing `PN`, `Description`, and `Requested Quantity`; `Subtotal` configuration rows; `Total:` terminator.

Same 27-column ANZ-GENERIC layout as HP Bid. Only the local pricing columns are populated — no MSRP, term, date, serial, or FX columns.

| Col | Header | Lenovo value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … for parents; `"1.01"`, `"1.02"`, … for their components |
| B | Vendor Name | `"LENOVO"` | `vendor.ToUpperInvariant()` |
| D | Vendor Part Number | `vpn` | `PN`; configuration parent attributes come from the row after `Subtotal` |
| E | Description | `description` | `Description` from the parent/component row |
| F | Qty. | `qty` | `Requested Quantity` |
| H | MSRP | **blank** | Lenovo quotes carry no list-price column |
| I | Cost | `cost` | Configuration parent: preceding `Subtotal` per-unit price. Standalone parent: its own per-unit price. Children: `0` → `0.0001` sentinel |
| K | Margin | `margin` (Uplift only) | Written only on the `Uplift` profile; blank for No Calculation |
| R | Comments | `comments` | Parent only: `Solution ID: <SID>` from the most recent configurator marker; blank for children |

All other columns: blank. End-loop sentinel row after the last line item — col B = `"*"`, col D = `EndLoopWarning` constant.

**Key differences vs HP Bid:**
- **A quoted total exists** in the extended-price cell beside `Total:`. Validation genuinely compares `computed_total = Σ(cost × qty)` against it. Only parents carry a non-zero cost, so the computed total is the sum over parent rows.
- Configuration blocks close arithmetically once their component per-unit prices reconcile to the subtotal. Positive priced rows after closure become standalone parents; blank/zero rows remain children.
- Child component rows are always cost `0` → written as the `0.0001` sentinel in column I (like HP's Bundle Detail rows).
- MSRP (col H) and Min Order Qty (col W) are never populated.

---

## Lenovo LBP-I ISG / LBP-I IDG Quote (ANZ-GENERIC — No Calculation / Uplift)

**Parsers:** `LenovoLbpiIsgPdfParser` (`lenovo_lbpi_isg_pdf`), `LenovoLbpiIdgPdfParser` (`lenovo_lbpi_idg_pdf`)  
**Template / Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (user-selected)

Both are PDF formats that write the same columns as Lenovo LBP-E above — local pricing only, no
MSRP, term, date, serial, or FX columns. Both write `LENOVO` in column B via `OutputVendorName`,
including the IDG parser, whose dropdown vendor is `Lenovo IDG`.

| Col | Header | LBP-I value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … parents; `"1.01"`, `"1.02"`, … children |
| B | Vendor Name | `"LENOVO"` | From `OutputVendorName` for both ISG and IDG |
| D | Vendor Part Number | `vpn` | IDG uses the placeholder `"SPEC"` when a component row carries no part label |
| E | Description | `description` | May wrap across lines or pages in the source PDF |
| F | Qty. | `qty` | |
| H | MSRP | **blank** | Neither format carries a list-price column |
| I | Cost | `cost` | Parents priced; children always `0` → `0.0001` sentinel |
| K | Margin | `margin` (Uplift only) | |
| R | Comments | `comments` | **ISG only:** `"Solution ID: <SID>"` on parents, blank on children. **IDG writes no comments** — it has no Solution IDs. |

**ISG vs IDG:**
- **ISG** derives one parent per Solution ID — the first numbered grid line beneath each `SID…` row,
  costed as the solution total ÷ parent quantity. Every other numbered line and every CONFIGURATION
  DETAILS component becomes a zero-cost child carrying the same `SolutionId`. Parts-only quotes
  (no `SID` rows) instead emit per-line priced parents with null Solution IDs.
- **IDG** emits one priced parent per product-grid line in document order, with its CONFIGURATION
  DETAILS components as zero-cost children. It has no Solution IDs, so
  `SupportsSolutionIdSplit` is `false` and the split option is not offered.

Both validate against the quote's grand total. See `docs/lenovo_lbpi_isg_pdf.md` and
`docs/lenovo_lbpi_idg_pdf.md`.

---

## Zebra PCR (ANZ-GENERIC — No Calculation / Uplift)

**Parsers:** `ZebraPriceConcessionPdfParser` (`zebra_pcr_pdf`), `ZebraPriceConcessionXlsParser` (`zebra_pcr_xls`)  
**Template / Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`  
**Sheet names:** `"No Calculation"` / `"Uplift"` (user-selected)

Same 27-column layout as HP. Differences from HP Bid:

| Col | Header | Zebra value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … — running index assigned at extract time |
| B | Vendor Name | `"ZEBRA"` | `vendor.ToUpperInvariant()` |
| D | Vendor Part Number | `vpn` | Source col `Part No.` |
| E | Description | `description` | Source col `Description`; may span multiple PDF lines or a page break |
| F | Qty. | `qty` | Source col `Min. Qty` (1 when blank) — or `1` for cancelled rows |
| H | MSRP | `msrp` | Source col `List Price`; **`NoBidPrice` (literal `0`) for cancelled rows** — not the sentinel |
| I | Cost | `cost` | Source col `Unit Special Price`; **`NoBidPrice` (literal `0`) for cancelled rows** — not the sentinel |
| K | Margin | `margin` (Uplift only) | Written only on the `Uplift` profile |
| R | Comments | `comments` | `"Max Qty: {Max. Qty}"` for active rows (blank when `Max. Qty` is absent); `"Cancelled (Standard Price)"` for cancelled rows |
| W | Min Order Qty | `min_qty` | Source col `Min. Qty`; **blank for cancelled rows** |
| Z | On Cost % | `onCostPercent` (user-supplied, optional) | Written to all **non-cancelled** rows when the user enters a value; blank when omitted or for cancelled rows |

All other columns: blank.

**Cancelled-row rules (`IsCancelled = true`):**
- Col H (MSRP) and col I (Cost) are written as `TemplateLayout.NoBidPrice` — a **literal `0`**, deliberately not the `0.0001` sentinel. A cancelled line belongs on the quote but is not bid-priced, so we *want* CRM to reject the `0` and pull the line's standard price from SAP. See "Zero pricing: the two zeros".
- Col F (Qty) is written as `1` regardless of the source qty.
- Col W (Min Order Qty) is left blank.
- Col Z (On Cost %) is left blank.
- Col R (Comments) is written as `"Cancelled (Standard Price)"`.

**On Cost % (col Z):**
- User-supplied optional decimal at parse time (`onCostPct` form field).
- Written to every non-cancelled row when provided; blank otherwise.
- Not persisted between sessions.

**End-loop sentinel row:** after the last line item — col B = `"*"`, col D = `EndLoopWarning` constant.

**Key differences vs HP Bid:**
- MSRP (col H) is populated from `List Price` — HP Bid leaves col H blank
- Qty (col F) comes from `Min. Qty`, not `Max. Qty` — the deal ceiling is carried in col R as `"Max Qty: {n}"`
- Cancelled rows write `NoBidPrice` (literal `0`) in cols H/I and leave col W blank — the SAP-fallback zero, not the sentinel that Bundle Detail rows use
- Col Z (On Cost %) is written when the user provides a value
- `Matches = true` always (no quoted total in PCR documents)

---

## Datalogic, Epson, and Strike Quote PDF (ANZ-GENERIC — No Calculation / Uplift)

**Parsers:** `DatalogicQuotePdfParser` (`datalogic_quote_pdf`), `EpsonQuotePdfParser`
(`epson_quote_pdf`), `StrikeQuotePdfParser` (`strike_quote_pdf`)

**Template / Writer:** `CrmWriter.Write(items, outputPath, crmTemplate, options)`

**Sheet names:** `"No Calculation"` / `"Uplift"` (user-selected)

These formats use the standard local-currency layout and a flat running sequence.

| Col | Header | Value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … in source order |
| B | Vendor Name | `DATALOGIC`, `EPSON`, or `STRIKE` | Parser vendor uppercased by `ParseService` |
| D | Vendor Part Number | `Vpn` | Format-specific source product/part code |
| E | Description | `Description` | Reassembled wrapped source description |
| F | Qty. | `Qty` | Datalogic row quantity; Epson quote-level quantity; Strike row QTY |
| H | MSRP | `Msrp` | Datalogic source List Price; Epson/Strike genuine zero emitted as `0.0001` |
| I | Cost | `Cost` | Datalogic Target Unit Price; Epson Price per unit; Strike Price |
| K | Margin | `margin` (Uplift only) | Blank for No Calculation |
| W | Min Order Qty | `MinQty` | Datalogic Min Shipment Size; `1` for Epson/Strike |
| Z | On Cost % | `onCostPercent` (optional) | Written only when supplied |

All other columns are blank. The end-loop sentinel follows the final line. On Cost is a parser
capability, not a vendor-name check: the dashboard renders/submits it and `ParseService` forwards it
only when the selected concrete parser declares `SupportsOnCost`.

---

## Trellix Quote PDF and XLSM (ANZ-GENERIC — No Calculation / Uplift)

`TrellixQuotePdfParser` (`trellix_quote_pdf`) and `TrellixQuoteXlsmParser`
(`trellix_quote_xlsm`) use the same local-currency writer and flat
`LineSequence`. Neither offers an On Cost input. `No Calculation` is the default; `Uplift` also writes
the standard Margin value in column K.

| Col | Source | Output behaviour |
|---|---|---|
| A, B | Source order, parser vendor | Text items `"1"`, `"2"`, …; Vendor Name `TRELLIX` |
| D, E | `Channel SKU`, `Product Description` | All SKU whitespace removed; PDF wrapped description joined |
| F | `QTY Software of support (Nodes)` (PDF) / `QTY Software or Support (Nodes)` (XLSM), or `QTY Hardware` | Exactly one positive quantity |
| H, I | `Total MSRP / Qty`, `Cost Per Unit` | Unit decimals; a genuine zero uses the writer's `0.0001` sentinel |
| M | `Latest Serial Number` | Blank when the source cell is empty |
| P, Q | `Start Date`, `End Date` | Native Excel dates when present, `DD/MM/YYYY` display format |
| R | `Program Type`, `Terms Length` (PDF) / formatted `Selling Term` (XLSM), `Grant #s` / `Grant #'s` | Non-empty values joined in that order with `" | "`; term remains textual |

`LineItem.Term` remains null, so column N is blank. PDF validates against quote-level
`Total Distribution Cost`; XLSM validates the sum of `Final Disti Cost` line totals against
`Σ(Cost Per Unit × Qty)`. Neither total is written to an item column. The ordinary end-loop
sentinel row follows the final line. See `docs/trellix_quote_pdf.md` and
`docs/trellix_quote_xlsm.md` for source-layout details.

---

## Dell CTO (ANZ-GENERIC — No Calculation)

**Parser:** `DellCtoJsonParser` (`dell_cto_json`)

**Template / Writer:** `CrmWriter.Write(items, outputPath, CrmTemplates.NoCalculation, options)`

**Sheet name:** `"No Calculation"`

Uses the same 27-column layout and parent/child line sequencing (`1`, `1.01`, `1.02`, …) as HP Bid and Lenovo.

Every source item produces a parent. Child SKUs are omitted only when the parent line of business is
`Displays`, or when the child restates its parent — same SKU number, same list price, same sale
price — as defined in `docs/dell_cto_json.md`.

| Col | Header | Dell CTO value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … for parents; `"1.01"`, `"1.02"`, … for children |
| B | Vendor Name | `"DELL"` | `vendor.ToUpperInvariant()` |
| D | Vendor Part Number | `vpn` | `baseSkuNumber` for parent; `skuNumber` for child |
| E | Description | `description` | Non-blank `customProductName`, otherwise `productDescription`, for parent; `description` for child |
| F | Qty. | `qty` | `quantity` |
| H | MSRP | `msrp` | Parent writes `unitListPriceIncludingShipping`; child writes `0.0001` sentinel |
| I | Cost | `cost` | Parent writes `unitSalesPriceIncludingShipping`; child writes `0.0001` sentinel |

When a parent `isRebateEligible` is `false`, Col R includes `Not Dell Rebate Eligible` (alongside the estimated-delivery comment when present). The parse-success popup warns the user to review the impacted comments.

All other columns: blank. End-loop sentinel row after last line item: col B = `"*"`.

---

## Dell APOS (ANZ-GENERIC — No Calculation)

**Parser:** `DellAposJsonParser` (`dell_apos_json`)

**Template / Writer:** `CrmWriter.Write(items, outputPath, CrmTemplates.NoCalculation, options)`

**Sheet name:** `"No Calculation"`

Uses the 27-column ANZ-GENERIC layout. Unlike Dell CTO, APOS emits flat top-level skus (`1`, `2`, `3`, ...) sorted by service tag and populates native pricing + contract dates + device serials.

Every source SKU is emitted — the Dell CTO child-SKU filters do not apply here (see
`docs/dell_apos_json.md`).

| Col | Header | Dell APOS value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, `"3"`, … flat sequence assigned after sorting by service tag |
| B | Vendor Name | `"DELL"` | `vendor.ToUpperInvariant()` |
| D | Vendor Part Number | `vpn` | `skus[].skuNumber` |
| E | Description | `description` | `skus[].description + " - " + skus[].lineOfBusiness`; `Misc`, absent, or blank uses `skus[].description` only |
| F | Qty. | `qty` | `skus[].quantity` |
| H | MSRP | `msrp` | `skus[].unitListPrice` |
| I | Cost | `cost` | `skus[].unitSalesPrice` |
| P | Start Date | `start_date` | `skus[].serviceTags.newContractStartDate` (blank if sentinel) |
| Q | End Date | `end_date` | `skus[].serviceTags.newContractEndDate` (blank if sentinel) |
| M | Serial Number | `serial_number` | `skus[].serviceTags.serviceTagNumber` |

All other columns: blank. End-loop sentinel row after last line item: col B = `"*"`.

---

## Cisco CCW Quote (ANZ-GENERIC — No Calculation)

**Parser:** `CiscoCcwQuoteXlsParser` (`cisco_ccw_quote_xls`)  
**Template / Writer:** `CrmWriter.Write(items, outputPath, CrmTemplates.NoCalculation, options)`  
**Sheet name:** `"No Calculation"`

Uses the 27-column ANZ-GENERIC layout. Flattens Cisco's 3-level line numbers (`1.0`, `1.1`, `2.0.1`) to the repository 2-level sequence convention (`1`, `1.01`, `2.01`).

| Col | Header | Cisco CCW value | Notes |
|---|---|---|---|
| A | Item | `LineSequence` | `"1"`, `"2"`, … for parents; `"1.01"`, `"1.02"`, … for children |
| B | Vendor Name | `"CISCO"` | `vendor.ToUpperInvariant()` |
| D | Vendor Part Number | `vpn` | `Part Number` (col B) |
| E | Description | `description` | `Part Description` (col C), space-normalised |
| F | Qty. | `qty` | `Quantity` (col J) |
| H | MSRP | `msrp` | `Unit List Price (With Duration)` (col AF), rounded 2 dp |
| I | Cost | `cost` | `Unit Net Price` (col R), rounded 2 dp |
| R | Comments | `comments` | Composed continuation text + `"Included Item/Support: {Included Item}"` |

All other columns: blank. End-loop sentinel row after last line item: col B = `"*"`. Zero prices emit `0.0001` sentinel.
