using System;
using System.Diagnostics;
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
        private readonly string _ffmpegPath = "ffmpeg";

        private bool _useEncryption;

        public FilesController(ILogger<FilesController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _useEncryption = configuration.GetValue("UseEncryption", false);

            _rootPath = configuration.GetValue("ConnectionStrings:FileSystemRoot", "");
            _certPath = configuration.GetValue("EncryptionCertificate", "");
            _ffmpegPath = configuration.GetValue("FfmpegPath", "ffmpeg");


            logger.LogInformation("Using root path: {RootPath}", _rootPath);
            if (!Path.Exists(_ffmpegPath)) throw new FileNotFoundException("FFmpeg executable not found.", _ffmpegPath);
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

        [DisableRequestSizeLimit]
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
                if (System.IO.File.Exists(filePath))
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
                        FileSize = fileStream.Length
                    };
                }

                if (filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    // Generate a thumbnail for the video
                    string thumbnailDir = Path.Combine(dir, "thumbs");
                    if (!Directory.Exists(thumbnailDir)) Directory.CreateDirectory(thumbnailDir);
                    string thumbnailPath = Path.Combine(thumbnailDir, $"{Path.GetFileNameWithoutExtension(filename)}.jpg");
                    try
                    {
                        await GenerateVideoThumbnailAsync(filePath, thumbnailPath);
                        response.ThumbnailPath = thumbnailPath;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to generate video thumbnail for {FilePath}", filePath);
                    }
                }

                if (_useEncryption)
                    await Encrypter.Encrypt(filePath, _certPath);
            }

            return Ok(response);
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> Delete(string file)
        {
            var userRootPath = ResolveUserRootPath();

            var base64EncodedBytes = Convert.FromBase64String(file);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            appPath = UnifyAppPath(appPath);

            var filePath = Path.Combine(userRootPath, appPath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File does not exist.");

            var deletedDir = Path.Combine(userRootPath, "deleted");
            if (!Directory.Exists(deletedDir))
                Directory.CreateDirectory(deletedDir);

            var fileName = Path.GetFileName(filePath);
            var deletedFilePath = Path.Combine(deletedDir, fileName);

            // Ensure unique name in deleted folder
            int count = 1;
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            while (System.IO.File.Exists(deletedFilePath))
            {
                deletedFilePath = Path.Combine(deletedDir, $"{nameOnly}_{count}{extension}");
                count++;
            }

            System.IO.File.Move(filePath, deletedFilePath);

            return Ok(new { DeletedPath = deletedFilePath });
        }

        public class ThumbnailRequest
        {
            public string File { get; set; }
            public string SeekTime { get; set; } = "00:00:01.000";
        }

        [HttpPost("generate-thumbnail")]
        public async Task<IActionResult> GenerateThumbnail([FromBody] ThumbnailRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.File))
                return BadRequest("Missing file parameter.");

            var userRootPath = ResolveUserRootPath();

            var base64EncodedBytes = Convert.FromBase64String(request.File);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            appPath = UnifyAppPath(appPath);

            var filePath = Path.Combine(userRootPath, appPath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File does not exist.");

            var thumbnailDir = Path.Combine(Path.GetDirectoryName(filePath), "thumbs");
            if (!Directory.Exists(thumbnailDir))
                Directory.CreateDirectory(thumbnailDir);

            var thumbnailPath = Path.Combine(
                thumbnailDir,
                $"{Path.GetFileNameWithoutExtension(filePath)}.jpg"
            );

            try
            {
                await GenerateVideoThumbnailAsync(filePath, thumbnailPath, request.SeekTime ?? "00:00:01");
                return Ok(new { ThumbnailPath = thumbnailPath });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate video thumbnail for {FilePath} at {SeekTime}", filePath, request.SeekTime);
                return StatusCode(500, "Failed to generate thumbnail.");
            }
        }

        [HttpPost("generate-video-image")]
        public async Task<IActionResult> GenerateVideoImage([FromBody] ThumbnailRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.File))
                return BadRequest("Missing file parameter.");

            var userRootPath = ResolveUserRootPath();

            var base64EncodedBytes = Convert.FromBase64String(request.File);
            var appPath = System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
            appPath = UnifyAppPath(appPath);

            var filePath = Path.Combine(userRootPath, appPath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File does not exist.");

            var videoDirectory = Path.GetDirectoryName(filePath);
            var videoDirectoryName = new DirectoryInfo(videoDirectory).Name;

            // Place the images directory directly under the user folder
            var videoImagesDir = Path.Combine(userRootPath, $"{videoDirectoryName}_video_images");
            if (!Directory.Exists(videoImagesDir))
                Directory.CreateDirectory(videoImagesDir);

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var extension = ".jpg";
            string seekTimeStr = request.SeekTime.Replace(":", "");
            string imagePath = Path.Combine(videoImagesDir, $"{baseName}_{seekTimeStr}{extension}");

            try
            {
                await GenerateVideoThumbnailAsync(filePath, imagePath, request.SeekTime ?? "00:00:01");
                var fileInfo = new FileInfo(imagePath);

                return Ok(new
                {
                    FileName = Path.GetFileName(imagePath),
                    FilePathFull = imagePath,
                    FileSize = fileInfo.Length
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate video image for {FilePath} at {SeekTime}", filePath, request.SeekTime);
                return StatusCode(500, "Failed to generate video image.");
            }
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
        
        private async Task<string> GenerateVideoThumbnailAsync(string videoPath, string thumbnailPath, string seekTime = "00:00:01.000")
        {
            // Example: ffmpeg -y -ss 00:00:01 -i input.mp4 -frames:v 1 -q:v 2 output.jpg
            // -y is to overwrite output file if it exists
            var args = $"-y -ss {seekTime} -i \"{videoPath}\" -frames:v 1 -q:v 2 \"{thumbnailPath}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"FFmpeg failed: {output}");

            return thumbnailPath;
        }
    }
}