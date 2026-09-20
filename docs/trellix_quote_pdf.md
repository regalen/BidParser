# Trellix Quote (PDF)

- **Parser:** `TrellixQuotePdfParser`
- **Slug:** `trellix_quote_pdf`
- **Accepted MIME:** `application/pdf`
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Additional settings:** none

## Recognition and table extraction

Recognition requires `Trellix`, `Distributor Quote Report`, and the `Channel SKU` table heading.
The parser finds that heading on every table page, derives each column seam from the printed
heading positions, and reconstructs physical and wrapped rows with `PdfWordCollector` and
`PdfTableHelpers`. A price-bearing row begins a logical item; the printed `Line Item` number is not
an anchor because one supplied report omits it and another repeats a number. Repeated headers,
page footers, and the note after the table are excluded before rows are joined.

The source header is narrow: PdfPig emits `Terms Length` vertically as `Term / s / Lengt / h`,
splits `QTY Software of support (Nodes)` across five lines, and places some date and term values
slightly left of their heading. The parser uses measured heading seams with a 6pt left allowance.
Wrapped SKUs, grant numbers, dates, descriptions, and even a cost decimal can continue on the
next physical line. One report has a `Data Center` column; the others instead have an
`Original Serial Number` and `Currency` column. The parser resolves both layouts per page.

## Field mapping

| Source | `LineItem` / rule |
|---|---|
| `Channel SKU` | `Vpn`; remove all whitespace, including internal and wrapped whitespace |
| `Product Description` | `Description`; join wrapped text in reading order |
| `Latest Serial Number` | `SerialNumber`; blank becomes null |
| `Cost Per Unit` | `Cost`; parse the printed unit amount |
| `Start Date`, `End Date` | Nullable `DateOnly`; accept printed `M/d/yyyy` dates, including a wrapped year |
| `QTY Hardware`, `QTY Software of support (Nodes)` | `Qty`; exactly one must be a positive integer; blank counts as zero |
| `Total MSRP` | `Msrp = Total MSRP / Qty`, after quantity validation |
| `Program Type`, `Terms Length`, `Grant #s` | `Comments`, in that order, joined with `" | "`; omit blanks |
| Document order | `LineSequence = "1", "2", …` even if printed line numbers repeat |

`Terms Length` remains text (`1.000` stays `1.000`) and does not populate `Term`. All whitespace is
removed from Grant #s before it enters comments. `Raw` retains non-empty source cells under their
printed headings, including the pre-normalised Channel SKU. A line with both quantity fields
positive, neither positive, or a negative, fractional, or unreadable quantity fails with a typed
`ParseError("extract")`.

## Metadata and validation

The report explicitly prints `Quote Currency: AUD`. Missing, unreadable, or non-AUD currency fails
with `ParseError("currency")`. The anchored `Quote Number` supplies both `QuoteNumber` and the
revisionless bid identity via `BidMetadataCleaner`; unreadable quote identity degrades to an empty
quote number and null bid fields. `Total Distribution Cost` supplies `QuotedTotal`, and shared
validation compares it with `Σ(Cost × Qty)` at the standard 0.01 tolerance. A missing or unreadable
distribution total is a typed totals error. `Total MSRP` is not the validation source.

The shared local-currency writer puts `TRELLIX` in Vendor Name; `Uplift` additionally writes its
normal Margin column. Real zero prices use the shared `0.0001` write-time sentinel. No Trellix
logic is added to the writer or template profiles. Report guidance remains administrator-managed.

## Retained synthetic fixtures

| Fixture | Coverage |
|---|---|
| `Trellix_Quote_900001.pdf` | Nine lines over three table pages; repeated headings, Data Center layout, wrapped date/SKU/cost, repeated printed line number, zero prices; quoted total AUD 1,546.05 |
| `Trellix_Quote_900002.pdf` | Original Serial/Currency layout; no printed number on first line, hardware and software quantity paths, populated latest serial, blank end date, comments with optional fields; quoted total AUD 50.00 |

The browser sample is byte-identical to the first input fixture. The private source PDFs and
reference workbook remain outside the committed fixture set. The supplied legacy workbook also
contains an unrelated worksheet and older vendor, date, comment, and zero-price conventions;
the synthetic goldens follow the current shared writer and the mapping above.
