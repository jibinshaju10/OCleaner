using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WpfApp1.Services
{
    public record FoundFile(string Path, long Size, DateTime LastWrite, string Category);

    public class FileScanner
    {
        private static readonly string[] Extensions = new[]
        {
            ".tmp", ".log", ".cache", ".bak", ".old", ".crdownload", ".part", ".dmp"
        };

        public async Task<List<FoundFile>> ScanAsync(CancellationToken cancellationToken, IProgress<Double> progress)
        {
            return await Task.Run(() => ScanInternal(cancellationToken, progress), cancellationToken);
        }

        private List<FoundFile> ScanInternal(CancellationToken cancellationToken, IProgress<Double> progress)
        {
            var results = new List<FoundFile>();

            var candidateDirs = new List<(string Path, string Category)>();

            try
            {
                candidateDirs.Add((Path.GetTempPath(), "User Temp"));
            }
            catch { }

            try
            {
                var localTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
                candidateDirs.Add((localTemp, "User Temp"));
            }
            catch { }

            try
            {
                candidateDirs.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "Downloads"));
            }
            catch { }

            // Add Windows Temp if accessible
            try
            {
                var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                candidateDirs.Add((windowsTemp, "Windows Temp"));
            }
            catch { }

            progress.Report(.3);

            int progressSteps = candidateDirs.Count;

            double currentStep = 0.3;

            foreach (var (dir, category) in candidateDirs.Where(d => !string.IsNullOrWhiteSpace(d.Path)).DistinctBy(d => d.Path))
            {
                progress.Report(currentStep + .2);
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    if (!Directory.Exists(dir))
                        continue;

                    foreach (var file in SafeEnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        try
                        {
                            var fi = new FileInfo(file);
                            if (!fi.Exists)
                                continue;

                            // Include by extension or if located under temp-like folder
                            if (Extensions.Contains(fi.Extension, StringComparer.OrdinalIgnoreCase)
                                || IsUnderTempPath(fi.DirectoryName))
                            {
                                results.Add(new FoundFile(fi.FullName, fi.Length, fi.LastWriteTime, category));
                            }
                            else
                            {
                                // also include small files in downloads older than 30 days
                                if (dir.EndsWith("Downloads", StringComparison.OrdinalIgnoreCase)
                                    && fi.LastWriteTime < DateTime.Now.AddDays(-30)
                                    && fi.Length < 10 * 1024 * 1024)
                                {
                                    results.Add(new FoundFile(fi.FullName, fi.Length, fi.LastWriteTime, category));
                                }
                            }
                        }
                        catch { /* ignore per-file errors */ }
                    }
                }
                catch { /* ignore folder errors */ }
            }

            return results.OrderByDescending(f => f.Size).ToList();
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option)
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                IEnumerable<string> subDirs = Array.Empty<string>();
                IEnumerable<string> files = Array.Empty<string>();

                try
                {
                    files = Directory.GetFiles(dir, pattern);
                }
                catch { }

                foreach (var f in files)
                    yield return f;

                if (option == SearchOption.AllDirectories)
                {
                    try
                    {
                        subDirs = Directory.GetDirectories(dir);
                    }
                    catch { subDirs = Array.Empty<string>(); }

                    foreach (var d in subDirs)
                        stack.Push(d);
                }
            }
        }

        private static bool IsUnderTempPath(string? dir)
        {
            if (string.IsNullOrEmpty(dir))
                return false;

            try
            {
                var lower = dir.ToLowerInvariant();
                return lower.Contains("\\temp") || lower.Contains("\\tmp") || lower.Contains("\\cache");
            }
            catch
            {
                return false;
            }
        }

        public async Task<long> DeleteFilesAsync(IEnumerable<FoundFile> toDelete, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => DeleteInternal(toDelete, progress, cancellationToken), cancellationToken);
        }

        private long DeleteInternal(IEnumerable<FoundFile> toDelete, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            var list = toDelete.ToList();
            long freed = 0;
            int total = list.Count;
            int done = 0;

            foreach (var f in list)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    if (File.Exists(f.Path))
                    {
                        try
                        {
                            var fi = new FileInfo(f.Path);
                            long size = fi.Length;
                            File.Delete(f.Path);
                            freed += size;
                        }
                        catch { /* ignore delete errors */ }
                    }
                }
                catch { }

                done++;
                progress?.Report(total == 0 ? 1 : (double)done / total);
            }

            return freed;
        }
    }
}
