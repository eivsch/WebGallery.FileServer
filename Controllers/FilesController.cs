using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebGallery.FileServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly ILogger<FilesController> _logger;
        private readonly string _rootPath;

        private bool _useEncryption;
        private string certPath = "Certificates/WebGallerySettings.pfx";

        public FilesController(ILogger<FilesController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _rootPath = configuration.GetValue("ConnectionStrings:FileSystemRoot", "");
            _useEncryption = configuration.GetValue("UseEncryption", false);
        }

        [HttpGet("image")]
        public async Task<IActionResult> DownloadImage(string file)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(file);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            
            var path = Path.Combine(_rootPath, appPath);

            // var path = Path.Combine(_rootPath, "testalbum/tst/Upload.jpg");

            byte[] fileBytes;
            if (_useEncryption)
            {
                using var decryptedFileStream = await Decrypter.Decrypt(path, certPath);
                fileBytes = decryptedFileStream.ToArray();
            }
            else
            {
                fileBytes = System.IO.File.ReadAllBytes(path);
            }

            //System.IO.File.WriteAllBytes("/home/eivind/out-srv.jpg", fileBytes);
            
            return new FileContentResult(fileBytes, "image/jpeg");
        }

        [HttpGet("video")]
        public async Task<IActionResult> DownloadVideo(string file)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(file);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            
            var path = Path.Combine(_rootPath, appPath);

            byte[] fileBytes;
            if (_useEncryption)
            {
                using var decryptedFileStream = await Decrypter.Decrypt(path, certPath);
                fileBytes = decryptedFileStream.ToArray();
            }
            else
            {
                fileBytes = System.IO.File.ReadAllBytes(path);
            }

            return new FileContentResult(fileBytes, "video/mp4");
        }

        [HttpPost]
        public async Task<IActionResult> Upload()
        {
            FileUploadResponse response = null;

            foreach (var file in Request.Form.Files)
            {
                string filename = file.FileName;
                string folder = file.Name;

                var dir = Path.Combine(_rootPath, folder);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, filename);

                // Override 'filePath' and 'filename' if a file with the same name already exists
                if( System.IO.File.Exists(filePath))
                {
                    var filenameParts = filename.Split('.');
                    string namePart = filenameParts[0] + "_1";
                    string fileExtensionPart = filenameParts[1];

                    filename = string.Join('.', namePart, fileExtensionPart);
                    filePath = Path.Combine(dir, filename);
                }

                using (var fileStream = System.IO.File.Create(filePath))
                {
                    file.CopyTo(fileStream);
                    
                    response = new FileUploadResponse
                    {
                        FileName = filename,
                        FilePathFull = filePath,
                        FileSize =fileStream.Length
                    };
                }

                if (_useEncryption)
                    await Encrypter.Encrypt(filePath, certPath);
            }

            return Ok(response);
        }
    }
}