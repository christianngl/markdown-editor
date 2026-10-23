using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownEditor.Services
{
    /// <summary>Antwort von Claude: Text (Markdown) und ob die Ausgabe am Token-Limit abgeschnitten wurde.</summary>
    public sealed class ClaudeResult
    {
        public string Text;
        public bool Truncated;
    }

    /// <summary>Fehler mit einer für den Benutzer verständlichen Meldung.</summary>
    public sealed class ClaudeException : Exception
    {
        public ClaudeException(string message) : base(message) { }
    }

    /// <summary>
    /// Minimaler Client für die Claude Messages API (POST /v1/messages) – ohne NuGet-Pakete,
    /// nur mit HttpWebRequest und DataContractJsonSerializer, damit build.ps1 (csc.exe, .NET 4.8) weiter genügt.
    /// </summary>
    public static class ClaudeClient
    {
        const string Endpoint = "https://api.anthropic.com/v1/messages";
        const string ApiVersion = "2023-06-01";
        const string FallbackBeta = "server-side-fallback-2026-07-01";
        const int MaxTokens = 16000;

        // ---------------------------------------------------------------- JSON-Verträge
        // Antwortfelder werden vom Serializer per Reflection gesetzt.
#pragma warning disable 0649

        [DataContract]
        sealed class Request
        {
            [DataMember(Name = "model", Order = 1)] public string Model;
            [DataMember(Name = "max_tokens", Order = 2)] public int MaxTokens;
            [DataMember(Name = "system", Order = 3)] public string System;
            [DataMember(Name = "messages", Order = 4)] public Message[] Messages;
            [DataMember(Name = "output_config", Order = 5)] public OutputConfig OutputConfig;
            // Lehnt ein Sicherheitsfilter die Anfrage ab, beantwortet sie der Server mit einem passenden Ersatzmodell.
            [DataMember(Name = "fallbacks", Order = 6)] public string Fallbacks;
        }

        [DataContract]
        sealed class Message
        {
            [DataMember(Name = "role", Order = 1)] public string Role;
            [DataMember(Name = "content", Order = 2)] public string Content;
        }

        [DataContract]
        sealed class OutputConfig
        {
            [DataMember(Name = "effort")] public string Effort;
        }

        [DataContract]
        sealed class Response
        {
            [DataMember(Name = "content")] public ContentBlock[] Content;
            [DataMember(Name = "stop_reason")] public string StopReason;
        }

        [DataContract]
        sealed class ContentBlock
        {
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "text")] public string Text;
        }

        [DataContract]
        sealed class ErrorResponse
        {
            [DataMember(Name = "error")] public ErrorBody Error;
        }

        [DataContract]
        sealed class ErrorBody
        {
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "message")] public string Message;
        }
#pragma warning restore 0649

        // ---------------------------------------------------------------- Anfrage

        public static async Task<ClaudeResult> CompleteAsync(string apiKey, string model, string system, string userText, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ClaudeException("Kein API-Schlüssel hinterlegt.");

            // .NET Framework 4.8 aktiviert TLS 1.2 nicht in jedem Fall von selbst.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var body = new Request
            {
                Model = model,
                MaxTokens = MaxTokens,
                System = system,
                Messages = new[] { new Message { Role = "user", Content = userText } },
                OutputConfig = new OutputConfig { Effort = "medium" },
                Fallbacks = "default"
            };
            byte[] payload = Serialize(body);

            var req = (HttpWebRequest)WebRequest.Create(Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers["x-api-key"] = apiKey.Trim();
            req.Headers["anthropic-version"] = ApiVersion;
            req.Headers["anthropic-beta"] = FallbackBeta;
            req.ContentLength = payload.Length;

            HttpWebResponse errorResponse;
            using (ct.Register(req.Abort))
            {
                try
                {
                    using (Stream s = await req.GetRequestStreamAsync().ConfigureAwait(false))
                        await s.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);

                    using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                    {
                        string json = await ReadAll(resp).ConfigureAwait(false);
                        return ParseSuccess(json);
                    }
                }
                catch (WebException ex)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    errorResponse = ex.Response as HttpWebResponse;
                    if (errorResponse == null)
                        throw new ClaudeException("Keine Verbindung zur Claude-API (" + ex.Status + ").\n" + ex.Message);
                }
            }

            // Fehlerantwort außerhalb des catch-Blocks lesen (C# 5 erlaubt dort kein await).
            using (errorResponse)
            {
                string json = await ReadAll(errorResponse).ConfigureAwait(false);
                throw new ClaudeException(DescribeError((int)errorResponse.StatusCode, json));
            }
        }

        static ClaudeResult ParseSuccess(string json)
        {
            Response r = Deserialize<Response>(json);
            if (r == null) throw new ClaudeException("Die Antwort der Claude-API konnte nicht gelesen werden.");
            if (r.StopReason == "refusal")
                throw new ClaudeException("Claude hat die Anfrage abgelehnt.");

            var sb = new StringBuilder();
            if (r.Content != null)
                foreach (ContentBlock b in r.Content)
                    if (b != null && b.Type == "text" && b.Text != null) sb.Append(b.Text);

            string text = StripOuterFence(sb.ToString().Trim());
            if (text.Length == 0) throw new ClaudeException("Claude hat keinen Text zurückgegeben.");
            return new ClaudeResult { Text = text, Truncated = r.StopReason == "max_tokens" };
        }

        static string DescribeError(int status, string json)
        {
            ErrorResponse e = null;
            try { e = Deserialize<ErrorResponse>(json); }
            catch (Exception) { }
            string detail = e != null && e.Error != null && !string.IsNullOrEmpty(e.Error.Message) ? "\n\nDetails: " + e.Error.Message : "";

            switch (status)
            {
                case 400: return "Die Anfrage wurde abgelehnt (400). Ist der Modellname korrekt?" + detail;
                case 401: return "Der API-Schlüssel ist ungültig (401). Bitte unter „KI → API-Schlüssel / Modell…“ prüfen." + detail;
                case 403: return "Der API-Schlüssel hat keine Berechtigung für diese Anfrage (403)." + detail;
                case 404: return "Modell oder Endpunkt nicht gefunden (404). Bitte den Modellnamen prüfen." + detail;
                case 413: return "Die Notiz ist zu groß für eine Anfrage (413)." + detail;
                case 429: return "Zu viele Anfragen oder Kontingent erschöpft (429). Bitte kurz warten und erneut versuchen." + detail;
                case 529: return "Claude ist gerade überlastet (529). Bitte später erneut versuchen." + detail;
                default:
                    if (status >= 500) return "Serverfehler bei der Claude-API (" + status + "). Bitte später erneut versuchen." + detail;
                    return "Fehler der Claude-API (" + status + ")." + detail;
            }
        }

        /// <summary>Entfernt einen Codeblock (```markdown … ```), der die gesamte Antwort umschließt.</summary>
        static string StripOuterFence(string text)
        {
            if (!text.StartsWith("```") || !text.EndsWith("```") || text.Length < 6) return text;
            int firstBreak = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak < 0 || lastFence <= firstBreak) return text;
            string inner = text.Substring(firstBreak + 1, lastFence - firstBreak - 1);
            return inner.Contains("```") ? text : inner.TrimEnd();
        }

        // ---------------------------------------------------------------- Hilfsfunktionen

        static async Task<string> ReadAll(HttpWebResponse resp)
        {
            using (Stream s = resp.GetResponseStream())
            using (var reader = new StreamReader(s, Encoding.UTF8))
                return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        static byte[] Serialize<T>(T value)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
                return ms.ToArray();
            }
        }

        static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T;
        }
    }
}
