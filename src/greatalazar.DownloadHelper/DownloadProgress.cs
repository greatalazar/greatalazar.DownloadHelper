namespace greatalazar.DownloadHelper;

public class DownloadProgress<T>
{
	public T Sender { get; set; }

	public long? TotalSize { get; set; }
	public long TotalDownloaded { get; set; }

	public double PercentDownloaded => TotalSize == null
		? -1.0
		: Math.Round((double)TotalDownloaded / TotalSize.Value, 4);
}
