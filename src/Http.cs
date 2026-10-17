using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace StanniUpdater
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
        private const string UserAgent = "StanniUpdater";

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
            return host == "api.github.com" || host == "github.com";
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
            }

            return GetWithCurl(url, accept, maxBytes);
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

        private byte[] GetWithCurl(string url, string accept, int maxBytes)
        {
            string file = Path.GetTempFileName();
            try
            {
                string args = "--fail --silent --show-error --location --proto =https --max-redirs 5"
                              + " --max-time " + (_timeoutMs / 1000 * 3) + " --connect-timeout " + (_timeoutMs / 1000)
                              + " --max-filesize " + maxBytes
                              + " -A \"" + UserAgent + "\" -H \"Accept: " + accept + "\""
                              + " -o \"" + file + "\" \"" + url + "\"";
                var start = new ProcessStartInfo("curl", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };

                using (Process process = Process.Start(start))
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

                        throw new IOException("curl failed: " + error.Trim());
                    }
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
