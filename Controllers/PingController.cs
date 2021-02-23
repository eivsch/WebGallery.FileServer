using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WebGallery.FileServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PingController : ControllerBase
    {
        [HttpGet]
        public IActionResult Ping()
        {
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2("Certificates/WebGallerySettings.pfx");
            var privateKey = cert.GetRSAPrivateKey();

            return Ok("pong - cert OK");
        }
        
    }
}