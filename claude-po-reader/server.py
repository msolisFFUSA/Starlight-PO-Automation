from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import subprocess
import tempfile

ROOT = os.path.dirname(os.path.abspath(__file__))
PARSER = os.path.join(ROOT, "parse_po.ps1")
HOST = os.environ.get("PO_READER_HOST", "127.0.0.1")
PORT = int(os.environ.get("PO_READER_PORT", "8788"))


class Handler(SimpleHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/api/parse":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if length == 0:
            self.send_error(400, "No PDF received")
            return
        with tempfile.NamedTemporaryFile(suffix=".pdf", delete=False) as file:
            file.write(self.rfile.read(length))
            pdf = file.name
        try:
            run = subprocess.run(
                ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", PARSER, "-PdfPath", pdf],
                capture_output=True,
                text=True,
                timeout=180,
            )
            if run.returncode:
                raise RuntimeError(run.stderr.strip() or "Parser failed")
            payload = run.stdout.strip()
            json.loads(payload)
            body = payload.encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except Exception as exc:
            body = json.dumps({"error": str(exc)}).encode()
            self.send_response(400)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        finally:
            try:
                os.unlink(pdf)
            except OSError:
                pass


os.chdir(ROOT)
ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()
