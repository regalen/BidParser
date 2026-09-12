# Strike Quote (PDF)

- **Parser:** `StrikeQuotePdfParser`
- **Slug:** `strike_quote_pdf`
- **Vendor:** `Strike`
- **Accepted input:** PDF
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Optional output input:** On Cost %

## Recognition and extraction

The parser recognises `QUOTE REFERENCE` and the `Product Code / Description / QTY / Price / Value`
grid. A numeric QTY starts each logical line. Every item row that independently resolves five body
bands contributes a candidate grid; median boundaries are used so a short first product code cannot
pull the Product Code / Description boundary left for the whole quote. Rows are then reconstructed
across pages until the `Delivery` totals anchor. Repeated headers are not required, so multi-page
quotes whose later pages continue the table are supported.

Wrapped product codes use `TextCleaner.JoinWrapped`: fragments that are pieces of one printed token
join directly, while real spaces within a product code remain spaces. Descriptions join with spaces,
and wrapped decimal price fragments join directly.

| Source field | `LineItem` field |
|---|---|
| `Product Code` | `Vpn` |
| `Description` | `Description` |
| `QTY` | `Qty` |
| `Price` | `Cost` |
| Constant | `MinQty = 1`, `Msrp = 0` |
| Logical row order | `LineSequence` (`"1"`, `"2"`, …) |

The value directly below `QUOTE REFERENCE` is both the revisionless bid number and quote number.
Strike prints unit prices at lower precision than line values, so validation deliberately sums the
printed `Value` column. The comparable quoted total is `Total (ex. tax)` less numeric
`Delivery Charge Value`; a non-numeric delivery value contributes zero.

## Retained fixtures

| Sample | Lines | Quoted/computed merchandise total (AUD) | Notable case |
|---|---:|---:|---|
| `Strike_Quote_9202.pdf` | 4 | 63,585.00 | Wrapped product codes; primary golden fixture |
| `Strike_Quote_9201.pdf` | 19 | 61,064.57 | Multi-page table; product codes containing spaces |
| `Strike_Quote_9203.pdf` | 2 | 28,820.70 | Wrapped `$319.5` + `0` unit price |

The primary sample has a cell-by-cell `No Calculation` golden workbook. Parsed zero MSRP is written
as the CRM zero-price sentinel in column H; On Cost is written to column Z when supplied.

## PdfPig quirks

- Candidate rows use a 5pt minimum gutter, then the parser takes the median of every resolvable
  boundary. Rows whose product codes contain real spaces may not independently form five bands and
  are ignored as grid candidates without being omitted from extraction.
- The mixed-font `Strike Group Australia` and `Page N of M` footer tokens differ by only 0.38pt in
  their row coordinate. The shared footer filter removes the complete resolved 3.5pt visual line,
  not an arbitrary page-wide Y strip, before logical rows are built.
- A decimal can wrap after one digit (`$319.5` / `0`); price fragments join without spaces before
  numeric parsing. Real product-code spaces are preserved by `TextCleaner.JoinWrapped`.

## Golden expected output (characterisation)

### `Strike_Quote_9202` (4 items, AUD)

| Item | Part Number | Description | Quantity | List Price | Sale Price |
|---:|---|---|---:|---:|---:|
| 1 | CAS-STKPTA5P | Strike Protective Case For Samsung Galaxy Tab Active5 Pro Rugged Tablet Case Box XL | 500 | 0.00 | 47.93 |
| 2 | CAS-STKHTA5P | Strike Rugged Case with Hand Strap and Lanyard For Samsung Galaxy Tab Active5 Pro/4 Pro/ Pro Rugged Tablet Case Box XL | 500 | 0.00 | 38.03 |
| 3 | ACC-STKTTA5P | Strike Tempered Glass Screen Protector for Samsung Galaxy Tab Active5 Pro | 500 | 0.00 | 18.86 |
| 4 | ACC-STKATTA5P | Strike Anti Glare Tempered Glass Screen Protector for Samsung Galaxy Tab Active5 Pro | 500 | 0.00 | 22.37 |

`Min Order Qty = 1` on every line. Printed line values sum to the delivery-exclusive quoted total
of `63,585.00`, so validation matches.
