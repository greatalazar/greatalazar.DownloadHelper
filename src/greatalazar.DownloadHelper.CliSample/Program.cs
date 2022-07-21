using static System.Console;

namespace greatalazar.DownloadHelper.CliSample;

internal class Program
{
	static async Task Main(string[] args)
	{
		var fileUri = args[0];

		var tempDir = Path.Combine(Environment.CurrentDirectory, "Temp");

		if(!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

		var mpd = new MultiPartDownload(fileUri, tempDir, tempDir);

		WriteLine($"Initializing {nameof(MultiPartDownload)}");
		await mpd.Initialize();

		if (!mpd.Initialized)
			WriteLine($"Failed to initialize {nameof(MultiPartDownload)}");

		mpd.MaxSegmentLength = 1024 * 1024; //1 MB

		WriteLine("Starting download");
		await mpd.StartDownload();

		WriteLine("Start merging completed segements");
		await mpd.StartMergeCompletedDownloads();

		if (File.Exists(mpd.DestinationPath)) File.Delete(mpd.DestinationPath);

		WriteLine("Rename merged download to destination file name");
		File.Move(mpd.DownloadRanges.First().TempFilePath, mpd.DestinationPath);
	}
}
