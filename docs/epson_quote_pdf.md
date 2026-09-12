# Epson Quote (PDF)

- **Parser:** `EpsonQuotePdfParser`
- **Slug:** `epson_quote_pdf`
- **Vendor:** `Epson`
- **Accepted input:** PDF
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Optional output input:** On Cost %

## Recognition and extraction

The parser recognises `Contract` together with the `Epson Product Code` table header. It locates the
first product-code row and the `Terms` boundary by anchors, then derives the four column ranges from
body-content bands so centred, multi-line headings do not become assumed cell edges.

The quote-level phrase `up to <quantity>` supplies `Qty` for every product row; comma-separated
quantities are supported. Product code starts each logical row. Wrapped code and price fragments
join without spaces, while descriptions join with spaces. The source `Model` remains available in
`LineItem.Raw` but is not written to a canonical output field.

| Source field | `LineItem` field |
|---|---|
| `Epson Product Code` | `Vpn` |
| `Product Description` | `Description` |
| `Price per unit ($AUD ex GST)` | `Cost` |
| Quote-level `up to …` | `Qty` |
| Constant | `MinQty = 1`, `Msrp = 0` |
| Logical row order | `LineSequence` (`"1"`, `"2"`, …) |

`Contract No` is both the revisionless bid number and quote number. Epson PDFs carry no quoted
total, so the parser constructs a passing validation result directly (`QuotedTotal = null`,
`Difference = 0`) rather than invoking nullable-total validation.

## Retained fixtures

| Sample | Lines | Quote-level quantity | Notable case |
|---|---:|---:|---|
| `Epson_96000002.pdf` | 2 | 450 | Primary golden fixture |
| `Epson_96000001.pdf` | 1 | 200 | Single-line quote |
| `Epson_96000003.pdf` | 1 | 2,500 | Split currency token and thousands quantity |

The primary sample has a cell-by-cell `No Calculation` golden workbook. Parsed zero MSRP is written
as the CRM zero-price sentinel in column H; On Cost is written to column Z when supplied.

## PdfPig quirks

- A logical item starts with any product code matching `^[A-Z]\d[A-Z0-9-]+$`; it is not restricted
  to the `C31C…` printer codes in the fixtures, so `B12B…` options and `S015…` supplies remain
  independent priced lines.
- The four body bands use a measured 1pt gutter because Model and Product Description nearly touch:
  their narrowest retained corridor is 1.29pt (the other fixtures measure 3.09pt and 4.43pt).
  The real-fixture geometry test pins each boundary so a changed font/layout fails visibly.
- PdfPig can split the `$` from a price and commas from the quote-level quantity; extraction joins
  the price fragments and accepts comma-separated integers.

## Golden expected output (characterisation)

### `Epson_96000002` (2 items, AUD)

| Item | Part Number | Model (`Raw`) | Description | Quantity | List Price | Sale Price |
|---:|---|---|---|---:|---:|---:|
| 1 | C31CK50202 | TM-M30III-202 | USB/Ethernet Thermal Printer - Black | 450 | 0.00 | 275.00 |
| 2 | C31CD38002 | TM-T70II-002 | Built-in USB, Parallel, Front facing receipt printer | 450 | 0.00 | 275.00 |

`Min Order Qty = 1` on every line. The format has no quoted total; validation reports a match with
`QuotedTotal = null` and `Difference = 0`.
