using Microsoft.AspNetCore.Http;

namespace PdfTargetValidator.Models
{
    public class PdfUploadForm
    {
        public IFormFile File { get; set; }
        public string Payload { get; set; }
    }
}
