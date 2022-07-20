using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace greatalazar.DownloadHelper.FileServer.Controllers;

[ApiController]
[Route("[controller]")]
public class FileServerController : ControllerBase
{
	private readonly ILogger<FileServerController> _logger;

	int sampleDataSize = 1024 * 1024 * 100; //100 MB
	Random _random = new Random();

	byte[] sampleData;

	public FileServerController(ILogger<FileServerController> logger)
	{
		_logger = logger;

		sampleData = new byte[sampleDataSize];
		_random.NextBytes(sampleData);
	}

	public ActionResult DownloadFile()
	{
		return new FileContentResult(sampleData, "*/*");
	}
}