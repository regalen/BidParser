

XQ-4076249.pdf is an example of a Software Only (PDF) quote where we need to extract the line items and 

**Sample File:** XQ-4076249.pdf
**Template Name :** Software Only (PDF)

**Extracting Line Items**
The line items begin in a table with the top left cell containing text "Product Code".

**Part Number**
To extract the part number, you need to extract the value from that column but skip any rows that contain the value "Term-Months" as this is not an actual line item. In XQ-4076249.pdf, the values you would extract would be as follows. The text needs to be trimmed so there are no spaces and no wrapping onto multiple lines.

SW-NCM-STR-PR
SW-NCI-PRO-PR
SW-NCI-PRO-PR
SW-NCI-E-PRO-PR
SW-NCM-E-STR-PR

**Description**
Using the "Product" column extract the text for each line but skip any line with the string "Term in months" as this is not an actual line item. When extracting the text it will be wrapped over multiple lines and be like this;

*Subscription, Nutanix Cloud
Manager (NCM) Starter
Software License &
Production Software Support
Service for 1 CPU Core*

You will need to clean this string up so it is all on one line and formatted correctly for display to a customer.

*Subscription, Nutanix Cloud Manager (NCM) Starter Software License & Production Software Support Service for 1 CPU Core*

**Term**
Using the "Term (Months)" column extract the value for each line but skip any line where the Product Code column contains "Term-Months". In the sample, you will see the value "60" for each line which is what you need to extract.

**MSRP**
Using the "List Unit Price" column extract the number in each row excluding rows where the Product Code contains "Terms-Months". The string in this field will look like "USD 383.00" or "USD 2,275.00".

You will need to strip out the curreny text "USD" and capture only the value. For example "USD 2,275.00" needs to be captured as "2275" and "USD 383.00" would be "383".

**Cost Price**
Using the "Net Unit Price" column extract the number in each row excluding rows where the Product Code contains "Terms-Months". The string in this field will look like "USD 101.11" or "USD 600.60".

You will need to strip out the curreny text "USD" and capture only the value. For example "USD 101.11" needs to be captured as "101.11" and "USD 600.60". would be "600.60".

**Quantity**
Using the "Quantity" column, extract the quantity but skip an lines where the Product Code column contains "Term-Months". In the provide sample XQ-4076249.pdf, you would end up with the values;

2096
864
1232
145
145

**Validation**
Once all lines are extracted, you will need to validate that the values are captured accurately. To achieve this I would first look to the "TOTAL: " field at the bottom of the able, in the sample file XQ-4076249.pdf this is wrapped onto the second page and contains the string "USD 1,625,358.51". You would need to strip the currency text "USD" and parse the raw number which is 1625358.51.

With the raw number, you would then calculate the total value using the extracted values for each line. You would multiply the value you extracted for Cost Price with Quantity and then add them all up. The resulting number should match the total you obtain of 1625358.51