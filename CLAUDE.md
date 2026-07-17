# Claude Code guidance

This repository contains two Windows implementations of the Starlight/Future Frame purchase-order reader:

- `Program.cs` is the original native Windows prototype.
- `claude-po-reader/` is the working local web reader. Prioritize this version for improvements.

## Local web reader

Run `start-local.cmd` from `claude-po-reader`, then open `http://127.0.0.1:8788`.

The reader accepts one Future Frame PO PDF at a time and shows only the description within parentheses with its printed `Total` quantity. It is Windows-only because it uses Windows OCR.

## Parser rules

- Preserve the printed top-to-bottom item order; never alphabetize results.
- Do not invent missing values. An item needs a parenthetical description and a nearby `Total` quantity.
- Handle continuation pages, which do not repeat the Quantity heading.
- Keep the 300 DPI primary scan and 150 DPI recovery scan. The recovery scan is only for a row missing from the primary scan.
- Keep user POs out of the repository. Use local copies only for verification.
- Before changing OCR matching, test with both a short one-page material PO and a multi-page PO with continuation items.
