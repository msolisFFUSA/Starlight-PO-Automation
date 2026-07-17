using System.Text.RegularExpressions;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

ApplicationConfiguration.Initialize();
Application.Run(new ReaderForm());

internal sealed record PurchaseOrderItem(string Description, int Quantity, int Page);
internal sealed record OcrWord(string Text, double X, double Y);

internal sealed class ReaderForm : Form
{
    private readonly Panel _dropZone = new() { Dock = DockStyle.Top, Height = 150, BackColor = Color.FromArgb(240, 248, 243) };
    private readonly Label _dropText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 8, 12, 0), ForeColor = Color.FromArgb(54, 78, 64) };
    private readonly DataGridView _table = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Button _browse = new() { Text = "Choose PDF", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };

    public ReaderForm()
    {
        Text = "Starlight Purchase Order Reader";
        Width = 940;
        Height = 650;
        MinimumSize = new Size(720, 450);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        _dropText.Text = "Drop a purchase-order PDF here\r\nor choose a PDF";
        _dropZone.Controls.Add(_dropText);
        _dropZone.AllowDrop = true;
        _dropZone.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _dropZone.DragDrop += async (_, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files?.FirstOrDefault() is string path) await ProcessFileAsync(path);
        };
        _dropZone.Click += async (_, _) => await BrowseAsync();
        _dropText.Click += async (_, _) => await BrowseAsync();

        var topBar = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(16, 10, 16, 8), BackColor = Color.White };
        var title = new Label { Text = "Purchase Order Reader", Dock = DockStyle.Left, AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.FromArgb(24, 54, 39) };
        _browse.Click += async (_, _) => await BrowseAsync();
        topBar.Controls.Add(_browse);
        topBar.Controls.Add(title);

        _table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PurchaseOrderItem.Description), HeaderText = "Item Description (from parentheses)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 76 });
        _table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PurchaseOrderItem.Quantity), HeaderText = "Quantity", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PurchaseOrderItem.Page), HeaderText = "Page", Width = 70 });

        Controls.Add(_table);
        Controls.Add(_status);
        Controls.Add(_dropZone);
        Controls.Add(topBar);
        SetStatus("Drop a Future Frame purchase-order PDF to extract its item descriptions and quantities.");
    }

    private async Task BrowseAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "PDF files (*.pdf)|*.pdf", Title = "Choose a purchase-order PDF" };
        if (dialog.ShowDialog(this) == DialogResult.OK) await ProcessFileAsync(dialog.FileName);
    }

    private async Task ProcessFileAsync(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { SetStatus("Please choose a PDF file.", true); return; }
        try
        {
            UseWaitCursor = true;
            SetStatus($"Reading {Path.GetFileName(path)}. This may take a moment...");
            var items = await Task.Run(() => ReadPurchaseOrderAsync(path));
            _table.DataSource = items;
            SetStatus(items.Count == 0 ? "No purchase-order items were recognized. Try a clearer PDF." : $"Finished: {items.Count} items, {items.Sum(x => x.Quantity)} total units.");
        }
        catch (Exception ex) { SetStatus($"Could not read the PDF: {ex.Message}", true); }
        finally { UseWaitCursor = false; }
    }

    private void SetStatus(string text, bool error = false) { _status.Text = text; _status.ForeColor = error ? Color.Firebrick : Color.FromArgb(54, 78, 64); }

    private static async Task<List<PurchaseOrderItem>> ReadPurchaseOrderAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var pdf = await PdfDocument.LoadFromFileAsync(file);
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Windows English OCR is not available.");
        var items = new List<PurchaseOrderItem>();
        for (uint pageIndex = 0; pageIndex < pdf.PageCount; pageIndex++)
        {
            using var page = pdf.GetPage(pageIndex);
            using var stream = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions { DestinationWidth = 1500, DestinationHeight = 1900 };
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(bitmap);
            items.AddRange(ParsePage(result.Lines.SelectMany(line => line.Words).Select(word => new OcrWord(word.Text, word.BoundingRect.X, word.BoundingRect.Y)).ToList(), (int)pageIndex + 1));
        }
        return items;
    }

    private static IEnumerable<PurchaseOrderItem> ParsePage(List<OcrWord> words, int page)
    {
        var quantityHeader = words.FirstOrDefault(w => string.Equals(w.Text, "Quantity", StringComparison.OrdinalIgnoreCase));
        if (quantityHeader is null) yield break;
        var seen = new HashSet<int>();
        foreach (var seed in words.Where(w => w.X < quantityHeader.X && w.Text.Contains('(')))
        {
            var row = words.Where(w => Math.Abs(w.Y - seed.Y) < 24 && w.X < quantityHeader.X).OrderBy(w => w.X).ToList();
            var text = string.Join(" ", row.Select(w => w.Text));
            var match = Regex.Match(text, @"\((.+)\)");
            if (!match.Success || !seen.Add((int)Math.Round(seed.Y / 10))) continue;
            var number = words.Where(w => w.X > quantityHeader.X && w.X < quantityHeader.X + 220 && Math.Abs(w.Y - seed.Y) < 30)
                .Select(w => w.Text.Trim().Equals("o", StringComparison.OrdinalIgnoreCase) ? "0" : w.Text)
                .FirstOrDefault(t => Regex.IsMatch(t, @"^\d+$"));
            yield return new PurchaseOrderItem(match.Groups[1].Value.Trim(), int.TryParse(number, out var quantity) ? quantity : 0, page);
        }
    }
}
