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
        private readonly string _certPath;

        private bool _useEncryption;

        public FilesController(ILogger<FilesController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _useEncryption = configuration.GetValue("UseEncryption", false);

            _rootPath = configuration.GetValue("ConnectionStrings:FileSystemRoot", "");
            _certPath = configuration.GetValue("EncryptionCertificate", "");
        }

        [HttpGet("image")]
        public async Task<IActionResult> DownloadImage(string file)
        {
            var userRootPath = ResolveUserRootPath();

            var base64EncodedBytes = System.Convert.FromBase64String(file);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            appPath = UnifyAppPath(appPath);

            var path = Path.Combine(userRootPath, appPath);

            byte[] fileBytes;
            if (_useEncryption)
            {
                using var decryptedFileStream = await Decrypter.Decrypt(path, _certPath);
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
            var userRootPath = ResolveUserRootPath();

            var base64EncodedBytes = System.Convert.FromBase64String(file);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            appPath = UnifyAppPath(appPath);
            
            var path = Path.Combine(userRootPath, appPath);

            byte[] fileBytes;
            if (_useEncryption)
            {
                using var decryptedFileStream = await Decrypter.Decrypt(path, _certPath);
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
            var userRootPath = ResolveUserRootPath();
            FileUploadResponse response = null;

            foreach (var file in Request.Form.Files)
            {
                string filename = Path.GetFileName(file.FileName);
                string folder = file.Name;

                var dir = Path.Combine(userRootPath, folder);
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
                    await Encrypter.Encrypt(filePath, _certPath);
            }

            return Ok(response);
        }

        private string UnifyAppPath(string appPath)
        {
            if (Path.DirectorySeparatorChar == '/')
                appPath = appPath.Replace('\\', '/');
            else
                appPath = appPath.Replace('/', '\\');

            return appPath;
        }

        private string ResolveUserRootPath()
        {
            if (Request.Headers.ContainsKey("Gallery-User"))
            {
                var userId = Request.Headers["Gallery-User"];

                return Path.Combine(_rootPath, userId);
            }

            return _rootPath;
        }
    }
}