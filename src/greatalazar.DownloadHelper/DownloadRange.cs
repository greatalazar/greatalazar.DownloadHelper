using System.Net.Http.Headers;

namespace greatalazar.DownloadHelper;

public class DownloadRange
{
	#region fields

	private readonly HttpClient _httpClient;

	private string _originalFileName;

	public Uri Uri { get; set; }
	public string TempFileName { get; private set; }
	public string TempFolder { get; set; }

	public int BufferSize { get; set; }

	public string TempFilePath => Path.Combine(TempFolder, TempFileName);

	public long From { get; set; }
	public long Length { get; set; }

	public long TotalLength { get; set; }

	public long DownloadedLength { get; set; } = 0;

	public long To => From + (Length - 1);

	public bool IsDownloadRunning { get; set; } = false;

	public bool IsCompleted => DownloadedLength == Length;

	#endregion

	#region ctor

	public DownloadRange(string uri, string fileName, long from, long length,
						 string tempFolder, HttpClient httpClient, long totalLength)
		: this(new Uri(uri), fileName, from, length, tempFolder, httpClient, totalLength)
	{ }

	public DownloadRange(Uri uri, string fileName, long from, long length, string tempFolder,
		HttpClient httpClient, long totalLength, int bufferSize = 1024 * 1024 * 4)
	{
		if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));

		Uri = uri;
		_originalFileName = fileName;
		From = from;
		Length = length;
		TempFolder = tempFolder;
		_httpClient = httpClient;
		TotalLength = totalLength;
		BufferSize = bufferSize;

		RefreshTempFileName();
	}

	#endregion

	#region methods

	public void RefreshTempFileName()
	{
		TempFileName = $"{_originalFileName}.{From},{Length}";
	}

	public async void StartDownloadRange(IProgress<DownloadProgress<DownloadRange>> progress, CancellationToken ct)
	{
		if (ct == default) throw new ArgumentNullException(nameof(ct));

		HttpRequestMessage hReqm = new(HttpMethod.Get, Uri);

		hReqm.Headers.Range = new RangeHeaderValue(
			From + DownloadedLength, To);

		HttpResponseMessage hRepm = await _httpClient.SendAsync(hReqm);

		if (!hRepm.IsSuccessStatusCode)
			throw new Exception($"Server returns with {hRepm.StatusCode}");
		else if (hRepm.StatusCode != System.Net.HttpStatusCode.PartialContent)
			throw new Exception("Server returns without requested ranges");

		IsDownloadRunning = true;

		try
		{
			using (Stream contentStream = await hRepm.Content.ReadAsStreamAsync())
			{
				using (FileStream fs = new FileStream(TempFilePath, FileMode.OpenOrCreate, FileAccess.Write))
				{
					if (fs.Length != Length)
						fs.SetLength(Length);

					int bufferSize = BufferSize;

					byte[] buffer = new byte[bufferSize];
					int readSize = 0;

					fs.Position = DownloadedLength;

					while ((readSize = await contentStream.ReadAsync(
						buffer, 0, bufferSize, ct).ConfigureAwait(false)) != 0)
					{
						bool shouldBreak = false;

						if (readSize + DownloadedLength > Length)
						{
							readSize = (int)(Length - DownloadedLength);
							shouldBreak = true;
						}

						await fs.WriteAsync(buffer, 0, readSize, ct);
						await fs.FlushAsync();
						DownloadedLength += readSize;

						if (progress != null)
						{
							progress.Report(new DownloadProgress<DownloadRange>()
							{
								Sender = this,
								TotalDownloaded = DownloadedLength,
								TotalSize = Length
							});
						}

						if (shouldBreak) break;

						ct.ThrowIfCancellationRequested();
					}
				}
			}
		}
		catch
		{
			IsDownloadRunning = false;
			throw;
		}
		finally
		{
			IsDownloadRunning = false;
		}
	}

	public async Task AddRange(DownloadRange downloadRange)
	{
		if (IsThisNextRange(downloadRange))
		{
			await this.MergeDownloadRangeFile(downloadRange);
			Length += downloadRange.Length;
			string oldTempFilePath = TempFilePath;
			RefreshTempFileName();
			File.Move(oldTempFilePath, TempFilePath);
		}
		else
		{
			throw new ArgumentException($"{downloadRange} is not next download range to {this}");
		}
	}

	public bool IsThisNextRange(DownloadRange downloadRange)
	{
		if (To + 1 == downloadRange.From) return true;
		return false;
	}

	#endregion

	#region override

	public override string ToString()
	{
		return $"From: {From}, To: {To}, Length: {Length}";
	}

	#endregion
}
