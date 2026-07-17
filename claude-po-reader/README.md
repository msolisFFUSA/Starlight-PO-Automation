# Claude-ready local PO reader

This is the working browser interface for Future Frame purchase orders. Drop one PDF into the page and it extracts the description inside parentheses with the matching printed `Total` quantity.

## Run locally

1. Install Python 3 on Windows.
2. Install Poppler and either add `pdftoppm.exe` to `PATH` or set `POPPLER_EXE` to its full path.
3. Double-click `start-local.cmd`.
4. Open `http://127.0.0.1:8788`.

To share it on a private network, run this first in Command Prompt:

```cmd
set PO_READER_HOST=0.0.0.0
start-local.cmd
```

Allow TCP port 8788 through Windows Firewall for the Private profile, then use your computer's local IPv4 address, such as `http://192.168.1.50:8788`.

## Notes for Claude Code

Read the repository-level `CLAUDE.md` before changing the parser. Keep PDFs and customer data out of Git.
