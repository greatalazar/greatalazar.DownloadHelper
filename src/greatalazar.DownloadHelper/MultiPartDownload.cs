using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace greatalazar.DownloadHelper;

public class MultiPartDownload
{
	#region fields

	private HttpClient _httpClient;
	private HttpClientHandler _httpClientHandler;

	public Uri Uri { get; set; }

	public Uri DownloadUri { get; set; }
	public CookieContainer CookieContainer { get; set; } = new CookieContainer();
	public ICredentials Credentials { get; set; }

	public string DestinationPath { get; set; }
	public long? Length { get; private set; } = 0;
	public bool Resumable { get; private set; } = false;
	public bool Unauthorized { get; set; } = true;

	public bool Initialized { get; set; } = false;

	private long LastIndex => Length.HasValue ? Length.Value - 1 : 0;

	public int MaxConnections { get; set; } = 4;
	public long MaxSegmentLength { get; set; } = 1024 * 1024;
	public int MaxErrorCount { get; set; } = 10;
	public int BufferSize { get; set; } = 1024 * 1024;

	public CancellationTokenSource DownloadCancellationTokenSource { get; set; } = new CancellationTokenSource();
	public CancellationTokenSource MergeDownloadedFilesCancellationTokenSource { get; set; } = new CancellationTokenSource();

	public int MaxRecursiveInitCount { get; set; } = 10;

	public string DestinationDirectory
	{
		get => Directory.Exists(DestinationPath)
			? DestinationPath
			: Path.GetDirectoryName(DestinationPath);
		set
		{
			if (DestinationDirectory != value)
				DestinationPath = Path.Combine(value, DestinationFileName);
		}
	}

	public string DestinationFileName
	{
		get => Directory.Exists(DestinationPath)
				? string.Empty
				: Path.GetFileName(DestinationPath);
		set
		{
			if (DestinationFileName != value)
				DestinationPath = File.Exists(DestinationPath)
					? Path.Combine(DestinationDirectory, value)
					: Path.Combine(DestinationPath, value);
		}
	}

	public string TempFolder { get; set; }

	private List<DownloadRange> downloadRanges = new List<DownloadRange>();
	private object downloadRangesLock = new object();
	public List<DownloadRange> DownloadRanges
	{
		get
		{
			lock (downloadRangesLock)
				return downloadRanges;
		}
		private set
		{
			lock (downloadRangesLock)
				downloadRanges = value;
		}
	}

	private List<DownloadRange> uncompletedDownloadRanges = new List<DownloadRange>();
	private object uncompletedDownloadRangesLock = new object();
	public List<DownloadRange> UncompletedDownloadRanges
	{
		get
		{
			lock (uncompletedDownloadRangesLock)
				return uncompletedDownloadRanges;
		}
		private set
		{
			lock (uncompletedDownloadRangesLock)
				uncompletedDownloadRanges = value;
		}
	}

	public HttpStatusCode InitializationResult { get; set; }
	public string InitializationLink { get; set; }

	public bool IsCompleted => DownloadRanges.Count() > 0 && DownloadRanges[0].Length == Length;

	public DownloadProgress<MultiPartDownload> DownloadProgress
	{
		get
		{
			return new DownloadProgress<MultiPartDownload>()
			{
				Sender = this,
				TotalDownloaded = DownloadRanges.Select(x => x.DownloadedLength).Sum(),
				TotalSize = Length
			};
		}
	}

	public bool IsNextRangeAvailable
	{
		get
		{
			bool op = true;

			if (DownloadRanges.Where(x => x.IsCompleted).Count() > 0 |
				DownloadRanges.Where(x => !x.IsCompleted).Count() > 0)
			{
				op = !DownloadRanges.Exists(x => x.To == LastIndex);
			}

			return op;
		}
	}

	#endregion

	#region ctor

	public MultiPartDownload(string uri, string destinationPath, string tempFolder) :
		this(new Uri(uri), destinationPath, tempFolder)
	{ }

	public MultiPartDownload(Uri uri, string destinationPath, string tempFolder)
	{
		Uri = uri;
		DestinationPath = destinationPath;
		TempFolder = tempFolder;

		if (!Directory.Exists(tempFolder))
			Directory.CreateDirectory(tempFolder);
	}

	#endregion

	#region downloader methods

	public async Task StartDownload()
	{
		if (!Initialized)
			throw new Exception("not initialized");

		InitHttpClient();

		int errorCount = 0;

		while (errorCount <= MaxErrorCount &&
			(DownloadRanges.Where(x => x.IsCompleted).Count() == 0) ||
				(DownloadRanges.Where(x => !x.IsCompleted).Count() != 0))
		{
			if (IsNextRangeAvailable &&
				DownloadRanges.Where(x => !x.IsCompleted || x.IsDownloadRunning == true).Count() < MaxConnections)
			{
				for (int i = DownloadRanges.Where(x => !x.IsCompleted || x.IsDownloadRunning == true).Count(); i < MaxConnections; i++)
				{
					if (!IsNextRangeAvailable) break;

					DownloadRange nextRange = GetNextRange(MaxSegmentLength);
					DownloadRanges.Add(nextRange);

					Progress<DownloadProgress<DownloadRange>> progress = new Progress<DownloadProgress<DownloadRange>>((downloadProgress) =>
					{
						//TODO: handle progress here or remove it completely
					});

					nextRange.StartDownloadRange(progress, DownloadCancellationTokenSource.Token);

					DownloadCancellationTokenSource.Token.ThrowIfCancellationRequested();
				}
			}
		}
	}

	public void StopDownload()
	{
		DownloadCancellationTokenSource.Cancel();
		DownloadCancellationTokenSource = new CancellationTokenSource();
	}

	#endregion

	#region methods

	public void InitHttpClient()
	{
		//TODO: add a way to pass client handler or httpClient

		if (_httpClient != null) _httpClient.Dispose();
		if (_httpClientHandler != null) _httpClientHandler.Dispose();

		_httpClientHandler = new HttpClientHandler();

		if (Debugger.IsAttached)
			_httpClientHandler.Proxy = new WebProxy("localhost", 8888);

		if (CookieContainer == null) CookieContainer = new CookieContainer();
		_httpClientHandler.CookieContainer = CookieContainer;
		_httpClientHandler.UseCookies = true;

		if (Credentials != null)
			_httpClientHandler.Credentials = Credentials;

		_httpClientHandler.AllowAutoRedirect = false;

		_httpClient = new HttpClient(_httpClientHandler);

		_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
		_httpClient.DefaultRequestHeaders.AcceptCharset.Add(new StringWithQualityHeaderValue("*"));
		_httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));

		//TODO: change to the current thread culture or remove it completely
		_httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));

		//TODO: get version from assemlby or option
		_httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(new ProductHeaderValue("greatalazar.Downloader", "0.0.0.0")));

		//TODO: make this optional
		_httpClient.DefaultRequestHeaders.Referrer = new Uri(Uri.AbsoluteUri.Remove(
			Uri.AbsoluteUri.Length - Uri.Segments.Last().Length - Uri.Query.Length));
	}

	public async Task Initialize(int initLoopCount = 0)
	{
		InitHttpClient();

		HttpRequestMessage hReqm = new HttpRequestMessage(HttpMethod.Head,
			DownloadUri == null ? Uri : DownloadUri);

		HttpResponseMessage hRepm = await _httpClient.SendAsync(hReqm);

		if (hRepm.IsSuccessStatusCode)
		{
			if (hRepm.Content.Headers.ContentLength.HasValue)
				Length = hRepm.Content.Headers.ContentLength.Value;
			else
				Length = 0;

			if (hRepm.Content.Headers.ContentDisposition != null && string.IsNullOrEmpty(hRepm.Content.Headers.ContentDisposition.FileName))
				DestinationFileName = hRepm.Content.Headers.ContentDisposition.FileName;
			else
			{
				try
				{
					DestinationFileName = Uri.UnescapeDataString(Uri.Segments.Last());
				}
				catch
				{
					if (string.IsNullOrEmpty(DestinationFileName))
						throw new Exception($"Couldn't identify {nameof(DestinationFileName)}");
				}
			}

			Resumable = hRepm.Headers.AcceptRanges.SingleOrDefault(x => x.Contains("bytes")) != null;

			Unauthorized = false;
			Initialized = true;
		}
		else
		{
			if (hRepm.StatusCode == HttpStatusCode.Unauthorized)
			{
				Unauthorized = true;
				Initialized = false;

				throw new UnauthorizedAccessException();
			}
			//TODO: edit this to StatusCode >= 300 & StatusCode <= 399
			else if (hRepm.StatusCode == HttpStatusCode.Redirect
				|| hRepm.StatusCode == HttpStatusCode.RedirectKeepVerb
				|| hRepm.StatusCode == HttpStatusCode.TemporaryRedirect
				|| hRepm.StatusCode == HttpStatusCode.SeeOther)
			{
				DownloadUri = hRepm.Headers.Location.IsAbsoluteUri
					? hRepm.Headers.Location
					: new Uri(Uri.GetLeftPart(UriPartial.Authority) + hRepm.Headers.Location);

				if (initLoopCount >= MaxRecursiveInitCount) throw new Exception($"{nameof(MaxRecursiveInitCount)} has reached");

				await Initialize(initLoopCount++);
			}
		}
	}

	public DownloadRange GetNextRange(long defaultLength)
	{
		DownloadRange op = null;

		if (DownloadRanges.Count > 0)
		{
			DownloadRange maxRange = DownloadRanges.OrderBy(x => x.To).Last();

			op = new DownloadRange(Uri, DestinationFileName, maxRange.To + 1,
				maxRange.To + defaultLength > LastIndex ? LastIndex - maxRange.To : defaultLength,
				TempFolder, _httpClient, Length.Value, bufferSize: BufferSize
			);
		}
		else
		{
			op = new DownloadRange(Uri, DestinationFileName, 0,
				defaultLength > Length ? Length.Value : defaultLength,
				TempFolder, _httpClient, Length.Value, bufferSize: BufferSize
			);
		}

		return op;
	}

	public async Task StartMergeCompletedDownloads()
	{
		while (DownloadRanges.Count > 1 && !MergeDownloadedFilesCancellationTokenSource.Token.IsCancellationRequested)
		{
			if (DownloadRanges.Count() > 1)
			{
				DownloadRange dr = DownloadRanges.SingleOrDefault(x => x.From == 0);

				if (dr != null)
				{
					DownloadRange adr = DownloadRanges.Where(x => x.From != 0 && !x.IsDownloadRunning && dr.IsThisNextRange(x)).SingleOrDefault();

					if (adr != null)
					{
						await dr.AddRange(adr);
						DownloadRanges.Remove(adr);
						File.Delete(adr.TempFilePath);
					}
				}
			}
		}
	}

	public void StopMergeCompletedDownloads()
	{
		MergeDownloadedFilesCancellationTokenSource.Cancel();
		MergeDownloadedFilesCancellationTokenSource = new CancellationTokenSource();
	}

	#endregion
}
