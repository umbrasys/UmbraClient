namespace UmbraSync.WebAPI.Files.Models;

public class FileDownloadStatus
{
    private int _downloadStatus;
    private long _totalBytes;
    private int _totalFiles;
    private long _transferredBytes;
    private int _transferredFiles;

    public DownloadStatus DownloadStatus
    {
        get => (DownloadStatus)Volatile.Read(ref _downloadStatus);
        set => Volatile.Write(ref _downloadStatus, (int)value);
    }

    public long TotalBytes
    {
        get => Interlocked.Read(ref _totalBytes);
        set => Interlocked.Exchange(ref _totalBytes, value);
    }

    public int TotalFiles
    {
        get => Volatile.Read(ref _totalFiles);
        set => Volatile.Write(ref _totalFiles, value);
    }

    public long TransferredBytes
    {
        get => Interlocked.Read(ref _transferredBytes);
        set => Interlocked.Exchange(ref _transferredBytes, value);
    }

    public int TransferredFiles
    {
        get => Volatile.Read(ref _transferredFiles);
        set => Volatile.Write(ref _transferredFiles, value);
    }

    public void AddTransferredBytes(long delta) => Interlocked.Add(ref _transferredBytes, delta);
}
