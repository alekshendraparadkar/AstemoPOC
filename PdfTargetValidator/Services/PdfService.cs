using System.IO;
using UglyToad.PdfPig;
using PdfTargetValidator.Interfaces;

namespace PdfTargetValidator.Services;

public class PdfService : IPdfService
{
    public string ExtractText(Stream pdfStream)
    {
        using var pdfDocument = PdfDocument.Open(pdfStream);

        return string.Join(" ",
            pdfDocument.GetPages().Select(p => p.Text));
    }
}
