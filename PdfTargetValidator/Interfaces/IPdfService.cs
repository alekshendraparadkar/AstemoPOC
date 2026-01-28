using System.IO;

namespace PdfTargetValidator.Interfaces;

public interface IPdfService
{
    string ExtractText(Stream pdfStream);
}