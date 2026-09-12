# Datalogic Quote (PDF)

- **Parser:** `DatalogicQuotePdfParser`
- **Slug:** `datalogic_quote_pdf`
- **Vendor:** `Datalogic`
- **Accepted input:** PDF
- **CRM templates:** `No Calculation` (default), `Uplift`
- **Optional output input:** On Cost %

## Recognition and extraction

The parser recognises the Datalogic identity and the `ID # / Part Number` table header. It locates
the body and `Grand Total` by anchors, then derives column boundaries from the whitespace corridors
between body-content bands. No row number or absolute column position is fixed.

`ID #` starts each logical line. Continuation rows are folded into that line: part-number fragments
join without spaces and description fragments join with spaces. `Min Shipment Size` applies to every
line. `Currency` must be AUD; a different or unreadable/missing currency fails closed with
`ParseError("currency")` rather than assuming AUD.

| Source field | `LineItem` field |
|---|---|
| `Part Number` | `Vpn` |
| `Description` | `Description` |
| `Qty` | `Qty` |
| `List Price` | `Msrp` |
| `Target Unit Price` | `Cost` |
| `Min Shipment Size` | `MinQty` |
| Logical row order | `LineSequence` (`"1"`, `"2"`, …) |

`Price Exception` supplies the revisionless bid number and `Quotation` supplies the quote number.
Missing metadata degrades to null/empty metadata; it does not invalidate an otherwise readable
table. Validation compares `Σ(Cost × Qty)` with `Grand Total` at the standard 0.01 tolerance.

## Retained fixtures

| Sample | Lines | Min order qty | Quoted/computed total (AUD) |
|---|---:|---:|---:|
| `Datalogic_PE930003.pdf` | 8 | 1 | 60,595.50 |
| `Datalogic_PE930001.pdf` | 3 | 10 | 209,397.00 |
| `Datalogic_PE930002.pdf` | 1 | 1 | 2,625.45 |

The primary sample has cell-by-cell golden workbooks for both `No Calculation` and `Uplift`.
On Cost is written to column Z when supplied; Uplift additionally writes margin to column K.

## PdfPig quirks

- The nine body bands use an 11pt gutter threshold. The narrowest measured inter-column corridor
  across the retained fixtures is 12.16pt, leaving normal word spaces inside a content band.
- A lone hyphen has a much shorter glyph box and can hang between two text baselines. Datalogic's
  pre-pass uses its horizontally adjacent full-height word to restore the printed line before
  descriptions are joined. This preserves `Wired - Charge Only` and `USB A - USB TYPE-C`.
- Wrapped source cells are joined before being stored in `LineItem.Raw`; the debugging value for a
  split part number therefore contains the complete part number, not only its anchor-row fragment.

## Golden expected output (characterisation)

### `Datalogic_PE930003` (8 items, AUD)

| Item | Part Number | Description | Quantity | List Price | Sale Price |
|---:|---|---|---:|---:|---:|
| 1 | 944950003 | MEMOR 17 051AC65AW2FT2AN GMS | 50 | 2,033.00 | 731.88 |
| 2 | 94A150128 | M3X, M12-17,SSD, Wired - Charge Only | 50 | 223.00 | 89.20 |
| 3 | 94ACC0327 | SKORPIO X5 CABLE USB A - USB TYPE-C | 50 | 39.00 | 17.55 |
| 4 | 94ACC0383 | PWR ADPT-WALL KIT,M3X,PS+4 PLUGS, USB-A | 50 | 60.00 | 27.00 |
| 5 | 94ACC0404 | M1X, Protective Boot | 50 | 84.00 | 37.80 |
| 6 | 94ACC0406 | M1X, SCAN TRIGGER HANDLE (w BOOT) | 50 | 221.00 | 88.40 |
| 7 | 94ACC0391 | M12/17, SCREEN PROTECTOR (5 PCS/UNIT) | 40 | 103.00 | 46.35 |
| 8 | ZSCFMEM1731 | MEMOR 17, FLEXI, 3 YEARS, COMPREHENSIVE | 50 | 305.00 | 183.00 |

`Min Order Qty = 1` on every line. `Grand Total = 60,595.50` and validation matches.
