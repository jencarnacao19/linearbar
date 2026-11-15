using System.Text;

namespace LinearBar.Core.Usage;

/// <summary>
/// Reads JSON-lines log files incrementally: each call returns only the complete lines that
/// were appended since the previous call. Session logs of coding agents grow to hundreds of
/// megabytes, so re-reading them every minute is not an option.
/// </summary>
public sealed class JsonlTail
{
    private readonly Dictionary<string, (long Offset, DateTime Modified)> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// New complete lines of every <c>*.jsonl</c> file under <paramref name="roots"/> that
    /// was modified after <paramref name="since"/>. Files that shrank are read again from
    /// the start.
    /// </summary>
    public IEnumerable<(string File, string Line)> ReadNew(IEnumerable<string> roots, DateTime since)
    {
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string file in files)
            {
                foreach (string line in ReadFile(file, since))
                {
                    yield return (file, line);
                }
            }
        }
    }

    /// <summary>Forgets every file, so the next read starts over.</summary>
    public void Reset() => _files.Clear();

    private List<string> ReadFile(string file, DateTime since)
    {
        var lines = new List<string>();
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.LastWriteTimeUtc < since.ToUniversalTime())
            {
                return lines;
            }

            (long offset, DateTime modified) = _files.GetValueOrDefault(file);
            if (info.Length < offset)
            {
                offset = 0;
            }

            if (info.Length == offset && info.LastWriteTimeUtc == modified)
            {
                return lines;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[64 * 1024];
            var pending = new List<byte>();
            long consumed = offset;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                int start = 0;
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                    {
                        continue;
                    }

                    pending.AddRange(new ArraySegment<byte>(buffer, start, i - start));
                    consumed += pending.Count + 1;
                    string line = Encoding.UTF8.GetString(pending.ToArray()).TrimEnd('\r');
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                    }

                    pending.Clear();
                    start = i + 1;
                }

                pending.AddRange(new ArraySegment<byte>(buffer, start, read - start));
            }

            // An unterminated last line is still being written; pick it up next time.
            _files[file] = (consumed, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return lines;
    }
}
