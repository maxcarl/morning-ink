# Kindle checklist

Manual check of an issue on a real Kindle. epubcheck in CI checks the format; this list checks what Amazon's conversion and the device make of it.

Send an issue to the device with Send to Kindle, by mail (the test mail in the settings) or through the web upload.

| Check | Expected | Result |
| --- | --- | --- |
| Delivery | Arrives without an error mail from Amazon | ✅ 2026-11-21 (web upload), ✅ 2026-11-22 (mail) |
| Title | "Morning Ink YYYY-MM-DD"; issues sort by date | ✅ 2026-11-22 by mail (the web upload shows the file name instead). Sorting of several real issues still to watch |
| Cover | The library shows the cover with title, date and headlines | ✅ 2026-11-21 |
| Contents | "Go to" lists the front page, sections and their articles | ✅ 2026-11-21 |
| Navigation | Jumps from the contents and the front page land on the right article | ✅ 2026-11-21 |
| Special characters | ä, ö, ü, ß and typographic quotes display correctly | ✅ 2026-11-21 |
| Images | Grayscale, full width, not cropped | ✅ 2026-11-21 |
| Links | "Read the original" opens the page in the Kindle browser | ✅ 2026-11-21 |
| Notices | Source failure notes appear on the front page | ✅ 2026-11-21 |
| Readability | Font size and typeface can be changed on the device | |
