

XQ-4128926.pdf is an example of a Renewal (PDF) quote where we need to extract the line items and 

**Sample File:** XQ-4128926.pdf
**Template Name :** Renewal (PDF)

**Extracting Line Items**
The line items begin in a table with the top left cell containing text "No".

**Part Number**
To extract the part number, you need to extract the value from the "Product Code" column. In XQ-4128926.pdf, the values you would extract would be as follows. The text needs to be trimmed so there are no spaces and no wrapping onto multiple lines.

RSW-NCM-STR-PR
RSW-NCI-ULT-PR
RSW-NCI-ULT-PR
RSW-NCM-STR-PR

**Serial Number**
Using the "Serial Number" column extract the text for each line. When extracting the text it will be wrapped over multiple lines and be like this;

24SW000351227,
LIC-02472987

You need to flatten this onto a single line, keeping the supplier's punctuation as-is. Join the wrapped fragments with no separator (the comma is already at the end of the first line). Trim leading/trailing whitespace and collapse any internal whitespace. The resulting value for the first row is:

24SW000351227,LIC-02472987

This value is stored on the line item as a single field `serial_number`. We do **not** split it into separate serial/license fields.

**Start Date**
Using the "Start Date" column to extract the value for each line. These are date strings that are in the format MM/DD/YYYY, for example 07/13/2026. You need to extract this string and convert it to DD/MM/YYYY which would make it 13/07/2026.

**End Date**
Using the "End Date" column to extract the value for each line. These are date strings that are in the format MM/DD/YYYY, for example 07/12/2027. You need to extract this string and convert it to DD/MM/YYYY which would make it 12/07/2027.

**MSRP**
Using the "Term Adjusted List Unit Price" column extract the number in each row. The string in this field will look like "USD 77.00" or "USD 575.00". Strip the "USD" prefix and any commas, and parse as a number. For example "USD 77.00" becomes "77" and "USD 575.00" becomes "575". In `XQ-4128926.pdf` the kept values are:

77
575
575
77

**Cost Price**
Using the "Net Unit Price" column extract the number in each row. The string in this field will look like "USD 54.41" or "USD 371.83". You will need to strip out the curreny text "USD" and capture only the value. For example "USD 54.41" needs to be captured as "54.41" and "USD 371.83". would be "371.83".

**Quantity**
Using the "Qty" column, extract the quantity. In the provide sample XQ-4128926.pdf, you would end up with the values;

160
32
72
160

**Validation**
Once all lines are extracted, you will need to validate that the values are captured accurately. To achieve this I would first look to the "TOTAL: " field at the bottom of the table, in the sample file XQ-4128926.pdf this contains the string "USD 60,205.68". You would need to strip the currency text "USD" and parse the raw number which is 60205.68.

With the raw number, you would then calculate the total value using the extracted values for each line. You would multiply the value you extracted for Cost Price with Quantity and then add them all up. The resulting number should match the total you obtain of 60205.68

## XQ-4166696.pdf Sample (Wrapped Prices)

This is a second example of a Renewal (PDF) quote, but with larger pricing amounts that trigger multi-line wrapping in the price columns (e.g. `USD` is printed on the first line of the cell, and the price amount like `1,121.00` wraps to the second line of the cell). Fusing the adjacent currency token `"USD"` and the numeric amount before column bucketing is required to prevent parsing issues.

### Extracted Line Items

| No | Product Code | Serial Number | Start Date | End Date | MSRP | Cost Price | Quantity | Total Net Price |
|----|--------------|---------------|------------|----------|------|------------|----------|-----------------|
| 1  | RSW-NCM-STR-PR | 25SW000430057,LIC-02537784 | 17/06/2026 | 01/12/2028 | 189.00 | 54.64 | 80 | 4,371.20 |
| 2  | RSW-NCI-PRO-PR | 25SW000430055,LIC-02537786 | 17/06/2026 | 01/12/2028 | 1,121.00 | 661.61 | 80 | 52,928.80 |
| 3  | RSW-NCM-STR-PR | 25SW000430056,LIC-02537783 | 28/10/2026 | 01/12/2028 | 161.00 | 40.20 | 400 | 16,080.00 |
| 4  | RSW-NCI-PRO-PR | 25SW000430054,LIC-02537785 | 28/10/2026 | 01/12/2028 | 955.00 | 755.64 | 400 | 302,256.00 |

### Validation

- Quoted Total: `USD 375,636.00`
- Computed Total: `(54.64 * 80) + (661.61 * 80) + (40.20 * 400) + (755.64 * 400) = 4,371.20 + 52,928.80 + 16,080.00 + 302,256.00 = 375,636.00`
- Matches: Yes