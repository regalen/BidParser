# Lenovo LBP-E ISG Quote (XLSX) — extraction spec

- **Parser class:** `BidParser.Parsing.Lenovo.LbpeIsgXls.LenovoLbpeIsgXlsParser`
- **Slug:** `lenovo_lbpe_isg_xls` (`ParserSlugs.LenovoLbpeIsgXls`)
- **Display name:** `LBP-E ISG Quote (XLSX)`
- **Vendor:** Lenovo (`Vendors.Lenovo`)
- **Accepted MIMEs:** `application/vnd.ms-excel`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Report type:** `Standard`

## File format and anchors

LBP-E ISG quotes are produced by Lenovo Bid Platform / Configurator exports in two container formats:
- Legacy OLE compound binary `.xls` workbooks
- Modern OpenXML spreadsheet `.xlsx` workbooks

Both formats are read through container-agnostic `ExcelReaderFactory.CreateReader(stream)`. The parser registers
`CodePagesEncodingProvider` in its static constructor and wraps non-Excel open failures in
`ParseError("detect", ...)` so an HTML-disguised or otherwise incorrect file selection is
handled as a wrong file type. Note: some Lenovo XLSX exports emit malformed `<dimension ref="A1"/>` metadata
while containing data as far down as row 290 in the current fixtures; `ExcelDataReader` streams through
`sheetData` directly, safely ignoring the dimension tag.

The line-item header is located by finding one row containing all three anchors `PN`,
`Description`, and `Requested Quantity`; row numbers are never fixed. Two price header families exist:
- **Legacy XLS**: `Adjusted Buy Price (AUD)` (or currency-agnostic `Adjusted Buy Price`)
- **Modern XLSX**: `Estimated Price (AUD)` (or currency-agnostic `Estimated Price`)

The first two matching columns are mapped positionally from left to right (first = per-unit price, second = extended price). Mixed price header families are rejected as corrupt.

| Header | Meaning |
|---|---|
| `PN` | Part number |
| `Description` | Description |
| `Requested Quantity` | Quantity |
| first `Adjusted Buy Price (AUD)` / `Estimated Price (AUD)` | Per-unit price |
| second `Adjusted Buy Price (AUD)` / `Estimated Price (AUD)` | Extended price |

The quote number is extracted from any cell matching
`Bid Request Number:\s*([A-Za-z0-9_-]+)` and falls back to the input filename stem. Currency
is always `AUD`.

## Row classification

The body is read in one forward pass.

| Row kind | Signature | Action |
|---|---|---|
| Solution marker | `PN` starts with `Set from Configurator` | Extract `\bSID[A-Za-z0-9]+` as the current Solution ID; do not emit |
| Configuration subtotal | `Description` equals `Subtotal` | Save the per-unit subtotal and mark the next data row as its parent |
| Feature header | `PN` = `Feature Code`, `Description` = `Description` | Skip |
| Terminator | Per-unit-price cell equals `Total:` | Read the quoted total from the extended-price cell and stop |
| Blank | All five mapped cells blank | Skip |
| Data | Any other row with a part number | Emit according to the active block state |

If the terminator is absent or its extended-price cell is blank, parsing fails with
`ParseError("totals", "Could not locate the 'Total:' row.", "Missing 'Total:' row.")`.

## Parent and child rules

For a configuration, the `Subtotal` row and the following data row form one parent:

- `Cost` comes from the subtotal's per-unit price.
- `Vpn`, `Description`, and `Qty` come from the following data row.
- `Comments` is `Solution ID: <SID>` when a marker has been seen.
- `SolutionId` carries the current identifier on the parent and all of its children.
- `LineSequence` is the next integer string: `1`, `2`, ...

All component rows beneath that parent are children, including component rows with a real
price. Their value is already included in the configuration subtotal:

- `Cost = 0m` in memory. `CrmWriter` converts it to the shared `0.0001` zero-price sentinel.
- `Vpn`, `Description`, and `Qty` come from the component row.
- `Comments = null`.
- `SolutionId` carries the same current identifier as the parent.
- `LineSequence = "{parent}.{child:D2}"`: `1.01`, `1.02`, ...

## Arithmetic block closure and standalone parents

Some workbooks put separately billable warranty, deployment, or licence choices after a configuration.
There is no blank row, grouping, indentation, or structural marker separating them from
included components. The parser therefore accumulates each configuration row's rounded
per-unit price until the running sum equals the configuration subtotal within `0.01`.

Once the block has reconciled:

- a row with a positive per-unit price starts a standalone parent, using its own row's price;
- a blank or zero-priced row remains a child of the current parent;
- a standalone parent resets the child counter and inherits the current Solution ID.

Worked example for `Bid_Platform_Bid_Request_Sample_01.xls`:

```text
20,314.55  configuration parent       accrued 20,314.55
     0.00  child                      accrued 20,314.55
   324.35  child                      accrued 20,638.90 == subtotal; block closes
        —  trailing SCY0              child (unpriced)
    85.69  3Yr KYD Add-On             standalone parent
   163.65  4Yr KYD Add-On             standalone parent
```

If a subtotal never reconciles, the block remains open until another `Subtotal` row or the
`Total:` terminator. Rows are retained as children, allowing normal total validation to expose
the mismatch rather than throwing away source data.

## Validation and expected fixtures

Money is rounded to two decimals using `MidpointRounding.AwayFromZero`. Validation calls
`ParseValidation.Validate(items, quotedTotal)`; children remain zero-cost in memory, so the
computed total is the sum of parent `Cost × Qty` only.

| Sample | Format | Parents | Children | Items | Quoted and computed total |
|---|---|---:|---:|---:|---:|
| `BRDAD019200001.xls` | XLS | 2 | 60 | 62 | 103,542.60 |
| `Bid_Platform_Bid_Request_Sample_03.xls` | XLS | 4 | 184 | 188 | 423,640.27 |
| `Bid_Platform_Bid_Request_Sample_01.xls` | XLS | 13 | 140 | 153 | 81,882.90 |
| `Bid_Platform_Bid_Request_Sample_02.xls` | XLS | 5 | 45 | 50 | 25,430.84 |
| `Bid_Platform_Bid_Request_Sample_04.xlsx` | XLSX | 1 | 38 | 39 | 71,380.91 |
| `Bid_Platform_Bid_Request_Sample_05.xlsx` | XLSX | 34 | 220 | 254 | 377,368.03 |
| `Bid_Platform_Bid_Request_Sample_06.xlsx` | XLSX | 4 | 152 | 156 | 183,932.90 |
| `Bid_Platform_Bid_Request_Sample_07.xlsx` | XLSX | 1 | 0 | 1 | 74,378.04 |
| `Bid_Platform_Bid_Request_Sample_08.xlsx` | XLSX | 3 | 110 | 113 | 354,578.02 |

For `BRDAD019200001.xls`, parent 1 is `7D7ACTO1WW`, quantity 1, cost `43,838.80`,
with children `1.01` through `1.30`; parent 2 is the same part number, quantity 1, cost
`59,703.80`, with children `2.01` through `2.30`. Both carry
`Solution ID: SIDX02SDL3`.

For `Bid_Platform_Bid_Request_Sample_04.xlsx`, parent 1 is `7D9CCTO1WW`, quantity 1, cost `71,380.91`, with children `1.01` through `1.38`, carrying `Solution ID: SIDTEST0001`.

## CRM output

The shared ANZ-GENERIC writer places `LineSequence` in column A as text, `Vpn` in D,
`Description` in E, `Qty` in F, `Cost` in I, and `Comments` in R. Parent costs are written
as parsed; every child cost becomes `0.0001`. Uplift additionally populates margin column K.
MSRP, term, dates, serial number, minimum quantity, and foreign-currency fields remain blank.

## Splitting by Solution ID

`LenovoLbpeIsgXlsParser.SupportsSolutionIdSplit` is `true`, so the dashboard offers **Split
output by Solution ID** for this format. When selected, the parsed line items are grouped by
their structured `SolutionId` in first-appearance order after parsing and before writing. A
null ID (including a workbook with no markers, such as standalone parts exports) forms its own group; splitting never turns a
valid parse into a failure.

Each group's line sequencing restarts from `1`: parent lines become `1`, `2`, `3`, … and each
parent's children become `1.01`, `1.02`, …. The normal writer then creates one workbook per
group. The HTTP response and retained history/monitoring output are a ZIP even when only one
group exists:

- archive: `<BidNumber>_<BidRevision>_<FileToken>.zip`
- workbook: `<BidNumber>_<BidRevision>_<solutionId>_<FileToken>.xlsx`
- null Solution ID token: `NoSolutionID`

If either bid field is unavailable, both names fall back to the previous `input_basename` prefix.

A split always produces **at least one** workbook — `SolutionOutputSplitter.Split` returns a
single empty group for an empty item list — so the archive is never empty and `X-Split-Count`
is never `0`.

The per-group workbooks are staged in an OS temp directory, not in the upload dir. Everything
under `outputs/` is reachable from a `ParseJob` row, which is all `RetentionService` can purge;
staging there would leak unreferenced files if the process were killed mid-write.

Validation remains whole-quote: `Σ(cost × qty)` is computed over the original complete item
list and compared with the source `Total:` once. The split workbooks are presentation outputs;
their subtotals are not independently revalidated.

## Bid metadata

Workbook prose supplies `Bid Request Number: ...` and `Version#: ...`; the revision is normalised. Best-effort: a missing `Version#:` keeps the number with revision `1`, and a missing bid request number leaves both null rather than failing the parse.
