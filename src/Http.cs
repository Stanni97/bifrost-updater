using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace BifrostUpdater
{
    /// <summary>
    /// Downloads with HttpWebRequest and falls back to the curl of the system. The Mono build inside
    /// Unity does not always know the root certificates of the machine, curl does.
    /// </summary>
    internal sealed class HttpStatusException : Exception
    {
        public HttpStatusException(int status, string message)
            : base(message)
        {
            Status = status;
        }

        public int Status { get; }
    }

    internal sealed class Http
    {
        private const string UserAgent = "BifrostUpdater";

        private readonly int _timeoutMs;
        private readonly Action<string> _debug;

        public Http(int timeoutSeconds, Action<string> debug)
        {
            _timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
            _debug = debug;
            try
            {
                // Tls12; the enum member does not exist in every runtime the game may run on.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception e)
            {
                _debug("Could not enable TLS 1.2: " + e.Message);
            }
        }

        public static bool IsAllowedUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            string host = uri.Host.ToLowerInvariant();
            return host == "api.github.com" || host == "github.com" || host == "thunderstore.io";
        }

        /// <summary>Returns the response body, or throws if neither HttpWebRequest nor curl got it.</summary>
        public byte[] Get(string url, string accept, int maxBytes)
        {
            if (!IsAllowedUrl(url))
            {
                throw new InvalidOperationException("Refusing to download from " + url);
            }

            try
            {
                return GetWithWebRequest(url, accept, maxBytes);
            }
            catch (WebException e) when (e.Status == WebExceptionStatus.ProtocolError && e.Response is HttpWebResponse answer)
            {
                // The server answered, so the connection works. curl would get the same answer.
                throw new HttpStatusException((int)answer.StatusCode, "HTTP " + (int)answer.StatusCode + " " + answer.StatusDescription);
            }
            catch (Exception e)
            {
                _debug("HttpWebRequest failed (" + e.GetType().Name + ": " + e.Message + "), trying curl.");
                try
                {
                    return GetWithTool(url, accept, maxBytes);
                }
                catch (HttpStatusException)
                {
                    throw;
                }
                catch (Exception tool)
                {
                    throw new IOException("HttpWebRequest: " + e.Message + " | " + tool.Message);
                }
            }
        }

        private byte[] GetWithWebRequest(string url, string accept, int maxBytes)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Accept = accept;
            request.Timeout = _timeoutMs;
            request.ReadWriteTimeout = _timeoutMs;
            request.AllowAutoRedirect = true;
            request.MaximumAutomaticRedirections = 5;

            using (WebResponse response = request.GetResponse())
            {
                // Redirects of release downloads stay on https, the final address is not restricted any further.
                if (response.ResponseUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("Redirected to a non-https address.");
                }

                using (Stream stream = response.GetResponseStream())
                using (var buffer = new MemoryStream())
                {
                    CopyLimited(stream, buffer, maxBytes);
                    return buffer.ToArray();
                }
            }
        }

        private static bool IsWindows => Path.DirectorySeparatorChar == '\\';

        /// <summary>Tries curl (Windows 10 ships curl.exe), then PowerShell on Windows or wget elsewhere.</summary>
        private byte[] GetWithTool(string url, string accept, int maxBytes)
        {
            int seconds = _timeoutMs / 1000;
            string curl = "--fail --silent --show-error --location --proto =https --max-redirs 5"
                          + " --max-time " + (seconds * 3) + " --connect-timeout " + seconds + " --max-filesize " + maxBytes
                          + " -A \"" + UserAgent + "\" -H \"Accept: " + accept + "\" -o \"{file}\" \"" + url + "\"";
            try
            {
                return RunDownloader("curl", curl, maxBytes);
            }
            catch (HttpStatusException)
            {
                throw;
            }
            catch (Exception curlError)
            {
                _debug("curl failed: " + curlError.Message);
                string second = IsWindows ? "powershell" : "wget";
                try
                {
                    return RunDownloader(second, IsWindows ? PowerShellArguments(url, accept, seconds) : WgetArguments(url, accept, seconds), maxBytes);
                }
                catch (HttpStatusException)
                {
                    throw;
                }
                catch (Exception secondError)
                {
                    throw new IOException("curl: " + curlError.Message + " | " + second + ": " + secondError.Message);
                }
            }
        }

        private static string WgetArguments(string url, string accept, int seconds)
        {
            return "--quiet --timeout=" + seconds + " --max-redirect=5 --user-agent=\"" + UserAgent + "\""
                   + " --header=\"Accept: " + accept + "\" -O \"{file}\" \"" + url + "\"";
        }

        /// <summary>
        /// Windows PowerShell (5.1 on every Windows 10/11, 3.0+ on Windows 7/8) with TLS 1.2 switched on.
        /// Everything inside the script uses single quotes, so the command line needs no escaping.
        /// </summary>
        private static string PowerShellArguments(string url, string accept, int seconds)
        {
            string script = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; "
                            + "try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; "
                            + "Invoke-WebRequest -UseBasicParsing -Uri '" + url.Replace("'", "''") + "' "
                            + "-Headers @{Accept='" + accept.Replace("'", "''") + "'} -UserAgent '" + UserAgent + "' "
                            + "-OutFile '{psfile}' -TimeoutSec " + seconds * 3 + " -MaximumRedirection 5 } "
                            + "catch { [Console]::Error.WriteLine($_.Exception.Message); if ($_.Exception.Response) { exit 2 } else { exit 1 } }";
            return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"";
        }

        private byte[] RunDownloader(string tool, string arguments, int maxBytes)
        {
            string file = Path.GetTempFileName();
            try
            {
                var start = new ProcessStartInfo(tool, arguments.Replace("{file}", file).Replace("{psfile}", file.Replace("'", "''")))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };

                // Steam starts the game with its own runtime libraries in these variables. The system's curl and wget
                // cannot load them ("version CURL_OPENSSL_4 not found"), so the tool gets a clean environment.
                start.EnvironmentVariables.Remove("LD_LIBRARY_PATH");
                start.EnvironmentVariables.Remove("LD_PRELOAD");

                Process started;
                try
                {
                    started = Process.Start(start);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    throw new IOException(tool + " is not installed");
                }

                using (Process process = started)
                {
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(_timeoutMs * 4) || process.ExitCode != 0)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // already gone
                        }

                        string reason = error.Trim().Length > 0 ? error.Trim() : tool + " failed";
                        // The server answered with an error (curl 22, wget 8, the PowerShell script's 2): the connection works.
                        if (process.HasExited && ((tool == "curl" && process.ExitCode == 22) || (tool == "wget" && process.ExitCode == 8) || (tool == "powershell" && process.ExitCode == 2)))
                        {
                            throw new HttpStatusException(0, reason);
                        }

                        throw new IOException(reason);
                    }
                }

                long size = new FileInfo(file).Length;
                if (size == 0)
                {
                    throw new IOException(tool + " returned no data");
                }

                if (size > maxBytes)
                {
                    throw new IOException("Download is larger than " + maxBytes + " bytes.");
                }

                return File.ReadAllBytes(file);
            }
            finally
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                    // temp file, the system cleans it up
                }
            }
        }

        private static void CopyLimited(Stream from, Stream to, int maxBytes)
        {
            var chunk = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = from.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    throw new IOException("Download is larger than " + maxBytes + " bytes.");
                }

                to.Write(chunk, 0, read);
            }
        }
    }
}
