# Cisco CCW Quote (XLS) extraction spec

- **Parser class:** `BidParser.Parsing.Cisco.CcwQuoteXls.CiscoCcwQuoteXlsParser`
- **Slug:** `cisco_ccw_quote_xls` (`ParserSlugs.CiscoCcwQuoteXls`)
- **Vendor:** Cisco (`Vendors.Cisco`)
- **Accepted MIME:** `application/vnd.ms-excel`
- **Supported file extensions:** `.xls`
- **CRM template:** `No Calculation` (`CrmTemplates.NoCalculation`)
- **Initial result guidance:** `Hardware SOH Disc %` wording, seeded in runtime configuration and editable by an administrator.
- **Sample fixture:** `samples/inputs/Quote_9400000001.xls`
- **Golden output:** `samples/outputs/Quote_9400000001_NoCalculation.xlsx`

## File format

Cisco CCW exports are legacy OLE2 binary compound documents (`D0 CF 11 E0`), read via `ExcelDataReader`. `CiscoCcwQuoteXlsParser` calls `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` in its static constructor to ensure legacy code-page string decoding.

## Guard order & error handling

0. **Workbook open:** `ExcelReaderFactory.CreateBinaryReader` is wrapped in a `try`/`catch`.
   - If the file is not a real OLE workbook: throws `ParseError("detect", ...)`. Magic-byte validation deliberately allows HTML through for `application/vnd.ms-excel` (Zebra ships styled HTML under a `.xls` name), so such a file reaches this parser and must read as a wrong-file-type rather than leaking ExcelDataReader's `HeaderException` as an unhandled failure. `LenovoLbpeIsgXlsParser` carries the same guard.
1. **Table header row:** Scans for the row containing all required header labels (`#`, `Part Number`, `Part Description`, `Quantity`, `Unit Net Price`, `Unit List Price (With Duration)`, `Included Item`).
   - If missing: throws `ParseError("detect", ...)`.
2. **Column mapping:** Maps indices by exact label.
   - If any required column label is missing: throws `ParseError("detect", ...)`.
3. **Currency validation:** Scans rows above the table header for a cell containing `"Currency:"` and checks the first non-empty cell to its right.
   - If currency is not `"AUD"`: throws `ParseError("currency", "Quote is not denominated in AUD.", ...)`.
   - **Fails closed:** if no `"Currency:"` value is found at all, throws `ParseError("currency", ...)` rather than proceeding. The ANZ-GENERIC template has no foreign-currency columns, so an unverified quote could silently write foreign amounts into local ones.

Checking table headers **before** currency validation ensures non-Cisco `.xls` files (e.g. Lenovo `.xls` files) trigger wrong-file-type (`detect`) reclassification instead of currency errors.

## Extraction algorithm

- **Metadata:** Quote ID extracted from `"Quote ID:"` header cell (falling back to filename without extension).
- **Termination:** Stops reading when col A contains `"Adjustments"`.
- **Line numbering:** Cisco's 3-level line sequence numbers (`1.0`, `1.1`, `2.0.1`) are flattened into the repository standard 2-level sequence (`1`, `1.01`, `2.01`).
  - Top-level parent: `#` that is a bare number (`1`) or has exactly one dot ending in `.0` (e.g. `1.0`, `2.0`, `12.0`). Note `3.10` is a **child**, not a parent.
  - Children: all other items, numbered `.01`, `.02`, … under the active parent in source order.
  - A child seen before any parent is **promoted to a parent** rather than dropped, so no source line is ever silently lost.
- **Field mapping:**
  - `LineSequence`: Derived 2-level sequence string.
  - `Vpn`: `Part Number` (col B).
  - `Description`: `Part Description` (col C), space-normalised via `TextCleaner.Clean`.
  - `Qty`: `Quantity` (col J).
  - `Msrp`: `Unit List Price (With Duration)` (col AF), rounded to 2 decimal places using `AwayFromZero`.
  - `Cost`: `Unit Net Price` (col R), rounded to 2 decimal places using `AwayFromZero`.
  - `Comments`: Composed continuation row text (if present) + `"Included Item/Support: {Included Item}"`.
  - `Description` is `null` (not `""`) when the source cell is blank, so the writer leaves the cell untouched.
- **Provenance (`Raw`):** holds the **source** cell text, never the derived value — `Raw["#"]` keeps Cisco's own `2.0.1` rather than the flattened `2.01`, and the price keys keep the unrounded figures (e.g. `16708.399999999998`, which `Msrp` rounds to `16708.40`). Continuation text is kept under `Raw["Continuation"]`.
- **Validation:** Subscription lines are priced per month, so sum of (unit cost × qty) does not equal the quote total cell. `ValidationResult` is constructed directly with `Matches = true, Difference = 0m, QuotedTotal = null` (same neutral pattern as HP Bid).

## Bid metadata

The `Quote ID:` header supplies `BidNumber`; revision is `1`. Best-effort — an absent header leaves both fields null rather than failing the parse.
