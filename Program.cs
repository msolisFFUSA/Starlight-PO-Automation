using System.Windows.Forms;
using Windows.Media.Ocr;

ApplicationConfiguration.Initialize();
Application.Run(new ReaderForm());

internal sealed class ReaderForm : Form
{
    public ReaderForm()
    {
        Text = "Starlight PO Automation";
        Width = 760;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;

        var message = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Font = new System.Drawing.Font("Segoe UI", 14),
            Text = "Purchase Order Reader\r\n\r\nNative Windows OCR is available and the PDF drop-reader interface is being added here.\r\n\r\nOCR image limit: " + OcrEngine.MaxImageDimension
        };
        Controls.Add(message);
    }
}
