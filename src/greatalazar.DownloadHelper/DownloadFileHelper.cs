namespace greatalazar.DownloadHelper;

public static class DownloadFileHelper
{
	/// <summary>
	/// Merge the range file into one
	/// </summary>
	/// <param name="tempFolder"></param>
	/// <param name="mainRange"></param>
	/// <param name="rangeToBeMerged"></param>
	/// <param name="bufferSize"></param>
	public static async Task MergeDownloadRangeFile(this DownloadRange mainRange,
		DownloadRange rangeToBeMerged, int bufferSize = 1024 * 1024 * 8)
	{
		using (FileStream mainfs = new(mainRange.TempFilePath, FileMode.Open))
		{
			using (FileStream fsToBeMerged = new(rangeToBeMerged.TempFilePath, FileMode.Open))
			{
				mainfs.SetLength(mainRange.Length + rangeToBeMerged.Length);
				mainfs.Position = rangeToBeMerged.From;
				await fsToBeMerged.CopyToAsync(mainfs, bufferSize);
				await fsToBeMerged.FlushAsync();
			}
		}
	}
}
