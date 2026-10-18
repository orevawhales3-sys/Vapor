using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DynamicOverlay.Poc
{
    public enum VaporPanelView
    {
        Browser = 0,
        ThreeDotMenu = 1,
        Shortcuts = 2,
        Notes = 3,
        Settings = 4,
        About = 5
    }

    public sealed class VaporTabItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string BadgeText { get; set; } = "W";
        public string BadgeBgHex { get; set; } = "#2563EB";
        public string BadgeFgHex { get; set; } = "#FFFFFF";
    }

    public sealed class ShortcutItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string BadgeText { get; set; } = "W";
        public string BadgeBgHex { get; set; } = "#2563EB";
        public string BadgeFgHex { get; set; } = "#FFFFFF";
    }

    public sealed class NoteDocument
    {
        public string Title { get; set; } = "Today - Notes";
        public string ContentText { get; set; } = string.Empty;
        public string XamlBase64 { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
        public string LastSavedDisplay { get; set; } = "Auto-saved";
    }

    public sealed class ShortcutsService
    {
        private readonly string _dataDirectory;
        private readonly string _shortcutsFilePath;
        private readonly List<ShortcutItem> _items = new List<ShortcutItem>();

        public ShortcutsService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentNullException("dataDirectory");
            }

            _dataDirectory = dataDirectory;
            Directory.CreateDirectory(_dataDirectory);
            _shortcutsFilePath = Path.Combine(_dataDirectory, "shortcuts.json");
            LoadShortcuts();
        }

        public string ShortcutsFilePath
        {
            get { return _shortcutsFilePath; }
        }

        public IReadOnlyList<ShortcutItem> GetShortcuts()
        {
            return _items.AsReadOnly();
        }

        public List<ShortcutItem> LoadShortcuts()
        {
            _items.Clear();
            try
            {
                if (!File.Exists(_shortcutsFilePath))
                {
                    SeedDefaultShortcuts();
                    SaveShortcutsToDisk();
                    return new List<ShortcutItem>(_items);
                }

                string raw = File.ReadAllText(_shortcutsFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    SeedDefaultShortcuts();
                    SaveShortcutsToDisk();
                    return new List<ShortcutItem>(_items);
                }

                List<string> objectBlocks = JsonLiteHelper.ExtractObjectBlocksFromArray(raw, "shortcuts");
                for (int i = 0; i < objectBlocks.Count; i++)
                {
                    string block = objectBlocks[i];
                    string id = JsonLiteHelper.ExtractString(block, "id");
                    string name = JsonLiteHelper.ExtractString(block, "name");
                    string url = JsonLiteHelper.ExtractString(block, "url");
                    string badgeText = JsonLiteHelper.ExtractString(block, "badgeText");
                    string badgeBg = JsonLiteHelper.ExtractString(block, "badgeBgHex");
                    string badgeFg = JsonLiteHelper.ExtractString(block, "badgeFgHex");

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    {
                        _items.Add(new ShortcutItem
                        {
                            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                            Name = name,
                            Url = url,
                            BadgeText = string.IsNullOrWhiteSpace(badgeText) ? DeriveBadgeText(name) : badgeText,
                            BadgeBgHex = string.IsNullOrWhiteSpace(badgeBg) ? DeriveBadgeColor(name) : badgeBg,
                            BadgeFgHex = string.IsNullOrWhiteSpace(badgeFg) ? "#FFFFFF" : badgeFg
                        });
                    }
                }
            }
            catch
            {
                if (_items.Count == 0)
                {
                    SeedDefaultShortcuts();
                }
            }

            return new List<ShortcutItem>(_items);
        }

        public static bool TryValidateAndNormalizeUrl(string rawUrl, out string normalizedUrl, out string errorMessage)
        {
            normalizedUrl = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                errorMessage = "Please enter a website URL.";
                return false;
            }

            string trimmed = rawUrl.Trim();
            if (trimmed.IndexOf(' ') >= 0)
            {
                errorMessage = "URL cannot contain spaces.";
                return false;
            }

            if (string.Equals(trimmed, "poc://diagnostics-test-page", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = "poc://diagnostics-test-page";
                return true;
            }

            string candidate = trimmed;
            if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.Contains("://"))
                {
                    errorMessage = "Only http:// and https:// URLs are supported.";
                    return false;
                }
                candidate = "https://" + candidate;
            }

            Uri? parsedUri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out parsedUri) || parsedUri == null)
            {
                errorMessage = "Please enter a valid website URL (e.g. https://github.com).";
                return false;
            }

            if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Only http:// and https:// URLs are supported.";
                return false;
            }

            string host = parsedUri.Host;
            if (string.IsNullOrWhiteSpace(host) ||
                (!host.Contains(".") && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)))
            {
                errorMessage = "Please enter a valid domain name (e.g. https://example.com).";
                return false;
            }

            normalizedUrl = parsedUri.AbsoluteUri;
            if (normalizedUrl.EndsWith("/", StringComparison.Ordinal) &&
                parsedUri.AbsolutePath == "/" &&
                string.IsNullOrEmpty(parsedUri.Query) &&
                string.IsNullOrEmpty(parsedUri.Fragment))
            {
                normalizedUrl = normalizedUrl.Substring(0, normalizedUrl.Length - 1);
            }

            return true;
        }

        public bool AddShortcut(string rawName, string rawUrl, out ShortcutItem? addedItem, out string errorMessage)
        {
            addedItem = null;
            string normalizedUrl;
            if (!TryValidateAndNormalizeUrl(rawUrl, out normalizedUrl, out errorMessage))
            {
                return false;
            }

            string finalName = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(finalName))
            {
                Uri? uri;
                if (Uri.TryCreate(normalizedUrl, UriKind.Absolute, out uri) && uri != null && !string.IsNullOrWhiteSpace(uri.Host))
                {
                    string host = uri.Host;
                    if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        host = host.Substring(4);
                    }
                    int dotIdx = host.IndexOf('.');
                    finalName = dotIdx > 0 ? host.Substring(0, dotIdx) : host;
                    if (finalName.Length > 0)
                    {
                        finalName = char.ToUpperInvariant(finalName[0]) + finalName.Substring(1);
                    }
                }
                else
                {
                    finalName = "Shortcut";
                }
            }

            ShortcutItem item = new ShortcutItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = finalName,
                Url = normalizedUrl,
                BadgeText = DeriveBadgeText(finalName),
                BadgeBgHex = DeriveBadgeColor(finalName),
                BadgeFgHex = "#FFFFFF"
            };

            _items.Add(item);
            if (!SaveShortcutsToDisk())
            {
                _items.Remove(item);
                errorMessage = "Failed to save shortcut to disk.";
                return false;
            }

            addedItem = item;
            return true;
        }

        public bool RemoveShortcut(string idOrNameOrUrl)
        {
            if (string.IsNullOrWhiteSpace(idOrNameOrUrl))
            {
                return false;
            }

            int index = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                if (string.Equals(_items[i].Id, idOrNameOrUrl, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_items[i].Name, idOrNameOrUrl, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_items[i].Url, idOrNameOrUrl, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return false;
            }

            ShortcutItem removed = _items[index];
            _items.RemoveAt(index);
            if (!SaveShortcutsToDisk())
            {
                _items.Insert(index, removed);
                return false;
            }

            return true;
        }

        public bool SaveShortcutsToDisk()
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"schemaVersion\": 1,");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"updatedAtUtc\": \"{0:O}\",\n", DateTime.UtcNow);
                sb.AppendLine("  \"shortcuts\": [");

                for (int i = 0; i < _items.Count; i++)
                {
                    ShortcutItem item = _items[i];
                    sb.AppendLine("    {");
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"id\": \"{0}\",\n", JsonLiteHelper.Escape(item.Id));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"name\": \"{0}\",\n", JsonLiteHelper.Escape(item.Name));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"url\": \"{0}\",\n", JsonLiteHelper.Escape(item.Url));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"badgeText\": \"{0}\",\n", JsonLiteHelper.Escape(item.BadgeText));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"badgeBgHex\": \"{0}\",\n", JsonLiteHelper.Escape(item.BadgeBgHex));
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      \"badgeFgHex\": \"{0}\"\n", JsonLiteHelper.Escape(item.BadgeFgHex));
                    sb.Append("    }");
                    if (i + 1 < _items.Count)
                    {
                        sb.AppendLine(",");
                    }
                    else
                    {
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                string tmp = _shortcutsFilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Copy(tmp, _shortcutsFilePath, true);
                try { File.Delete(tmp); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SeedDefaultShortcuts()
        {
            _items.Clear();
            _items.Add(new ShortcutItem { Id = "sc-youtube", Name = "YouTube", Url = "https://www.youtube.com", BadgeText = "▶", BadgeBgHex = "#DC2626", BadgeFgHex = "#FFFFFF" });
            _items.Add(new ShortcutItem { Id = "sc-github", Name = "GitHub", Url = "https://github.com", BadgeText = "GH", BadgeBgHex = "#1E293B", BadgeFgHex = "#F8FAFC" });
            _items.Add(new ShortcutItem { Id = "sc-chatgpt", Name = "ChatGPT", Url = "https://chat.openai.com", BadgeText = "AI", BadgeBgHex = "#0D9488", BadgeFgHex = "#FFFFFF" });
            _items.Add(new ShortcutItem { Id = "sc-google", Name = "Google", Url = "https://www.google.com", BadgeText = "G", BadgeBgHex = "#2563EB", BadgeFgHex = "#FFFFFF" });
            _items.Add(new ShortcutItem { Id = "sc-leetcode", Name = "LeetCode", Url = "https://leetcode.com", BadgeText = "<>", BadgeBgHex = "#D97706", BadgeFgHex = "#FFFFFF" });
            _items.Add(new ShortcutItem { Id = "sc-notion", Name = "Notion", Url = "https://www.notion.so", BadgeText = "N", BadgeBgHex = "#334155", BadgeFgHex = "#FFFFFF" });
            _items.Add(new ShortcutItem { Id = "sc-gmail", Name = "Gmail", Url = "https://mail.google.com", BadgeText = "M", BadgeBgHex = "#E11D48", BadgeFgHex = "#FFFFFF" });
        }

        public static string DeriveBadgeText(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "W";
            }

            string trimmed = name.Trim();
            if (trimmed.StartsWith("YouTube", StringComparison.OrdinalIgnoreCase)) return "▶";
            if (trimmed.StartsWith("GitHub", StringComparison.OrdinalIgnoreCase)) return "GH";
            if (trimmed.StartsWith("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "AI";
            if (trimmed.StartsWith("Google", StringComparison.OrdinalIgnoreCase)) return "G";
            if (trimmed.StartsWith("LeetCode", StringComparison.OrdinalIgnoreCase)) return "<>";
            if (trimmed.StartsWith("Notion", StringComparison.OrdinalIgnoreCase)) return "N";
            if (trimmed.StartsWith("Gmail", StringComparison.OrdinalIgnoreCase)) return "M";
            if (trimmed.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return "MS";

            return char.ToUpperInvariant(trimmed[0]).ToString(CultureInfo.InvariantCulture);
        }

        public static string DeriveBadgeColor(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "#2563EB";
            }

            string trimmed = name.Trim();
            if (trimmed.StartsWith("YouTube", StringComparison.OrdinalIgnoreCase)) return "#DC2626";
            if (trimmed.StartsWith("GitHub", StringComparison.OrdinalIgnoreCase)) return "#1E293B";
            if (trimmed.StartsWith("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "#0D9488";
            if (trimmed.StartsWith("Google", StringComparison.OrdinalIgnoreCase)) return "#2563EB";
            if (trimmed.StartsWith("LeetCode", StringComparison.OrdinalIgnoreCase)) return "#D97706";
            if (trimmed.StartsWith("Notion", StringComparison.OrdinalIgnoreCase)) return "#334155";
            if (trimmed.StartsWith("Gmail", StringComparison.OrdinalIgnoreCase)) return "#E11D48";
            if (trimmed.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return "#0284C7";

            string[] palette = new string[] { "#2563EB", "#7C3AED", "#0891B2", "#059669", "#D97706", "#E11D48" };
            int hash = Math.Abs(trimmed.GetHashCode());
            return palette[hash % palette.Length];
        }
    }

    public sealed class NotesService
    {
        private readonly string _dataDirectory;
        private readonly string _notesFilePath;
        private NoteDocument _currentNote;

        public NotesService(string dataDirectory)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                throw new ArgumentNullException("dataDirectory");
            }

            _dataDirectory = dataDirectory;
            Directory.CreateDirectory(_dataDirectory);
            _notesFilePath = Path.Combine(_dataDirectory, "notes.json");
            _currentNote = LoadNote();
        }

        public string NotesFilePath
        {
            get { return _notesFilePath; }
        }

        public NoteDocument CurrentNote
        {
            get { return _currentNote; }
        }

        public NoteDocument LoadNote()
        {
            try
            {
                if (!File.Exists(_notesFilePath))
                {
                    _currentNote = CreateDefaultNote();
                    string initialStatus;
                    SaveNote(_currentNote.Title, _currentNote.ContentText, _currentNote.XamlBase64, out initialStatus);
                    return _currentNote;
                }

                string raw = File.ReadAllText(_notesFilePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    _currentNote = CreateDefaultNote();
                    return _currentNote;
                }

                string title = JsonLiteHelper.ExtractString(raw, "title");
                string content = JsonLiteHelper.ExtractString(raw, "contentText");
                string xamlB64 = JsonLiteHelper.ExtractString(raw, "xamlBase64");
                string updatedUtc = JsonLiteHelper.ExtractString(raw, "updatedAtUtc");
                string lastSavedDisplay = JsonLiteHelper.ExtractString(raw, "lastSavedDisplay");

                _currentNote = new NoteDocument
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "Today - Notes" : title,
                    ContentText = content ?? string.Empty,
                    XamlBase64 = xamlB64 ?? string.Empty,
                    UpdatedAtUtc = updatedUtc ?? string.Empty,
                    LastSavedDisplay = string.IsNullOrWhiteSpace(lastSavedDisplay) ? "Auto-saved" : lastSavedDisplay
                };
                return _currentNote;
            }
            catch
            {
                _currentNote = CreateDefaultNote();
                return _currentNote;
            }
        }

        /// <summary>
        /// Atomically saves the note to disk and returns true ONLY after the file write succeeds.
        /// </summary>
        public bool SaveNote(string title, string contentText, string xamlBase64, out string savedStatusText)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                string finalTitle = string.IsNullOrWhiteSpace(title) ? "Today - Notes" : title.Trim();
                string finalContent = contentText ?? string.Empty;
                string finalXaml = xamlBase64 ?? string.Empty;
                DateTime nowLocal = DateTime.Now;
                DateTime nowUtc = DateTime.UtcNow;
                string display = "Auto-saved • " + nowLocal.ToString("h:mm tt", CultureInfo.InvariantCulture);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"schemaVersion\": 1,");
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"title\": \"{0}\",\n", JsonLiteHelper.Escape(finalTitle));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"contentText\": \"{0}\",\n", JsonLiteHelper.Escape(finalContent));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"xamlBase64\": \"{0}\",\n", JsonLiteHelper.Escape(finalXaml));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"lastSavedDisplay\": \"{0}\",\n", JsonLiteHelper.Escape(display));
                sb.AppendFormat(CultureInfo.InvariantCulture, "  \"updatedAtUtc\": \"{0:O}\"\n", nowUtc);
                sb.AppendLine("}");

                string tmp = _notesFilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Copy(tmp, _notesFilePath, true);
                try { File.Delete(tmp); } catch { }

                if (!File.Exists(_notesFilePath))
                {
                    savedStatusText = "Save failed";
                    return false;
                }

                _currentNote = new NoteDocument
                {
                    Title = finalTitle,
                    ContentText = finalContent,
                    XamlBase64 = finalXaml,
                    UpdatedAtUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture),
                    LastSavedDisplay = display
                };
                savedStatusText = display;
                return true;
            }
            catch
            {
                savedStatusText = "Save failed";
                return false;
            }
        }

        private static NoteDocument CreateDefaultNote()
        {
            string defaultBullets =
                "• Complete DSA questions\r\n" +
                "• Work on project UI\r\n" +
                "• College assignment\r\n" +
                "• Review design ideas\r\n" +
                "• Plan for next week";

            return new NoteDocument
            {
                Title = "Today - Notes",
                ContentText = defaultBullets,
                XamlBase64 = string.Empty,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                LastSavedDisplay = "Auto-saved • " + DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture)
            };
        }
    }

    internal static class JsonLiteHelper
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string ExtractString(string json, string fieldName)
        {
            string token = "\"" + fieldName + "\"";
            int keyIdx = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0)
            {
                return string.Empty;
            }

            int colonIdx = json.IndexOf(':', keyIdx + token.Length);
            if (colonIdx < 0)
            {
                return string.Empty;
            }

            int startQuote = json.IndexOf('"', colonIdx + 1);
            if (startQuote < 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = startQuote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(next); break;
                    }
                    i++;
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public static List<string> ExtractObjectBlocksFromArray(string json, string arrayFieldName)
        {
            List<string> results = new List<string>();
            string token = "\"" + arrayFieldName + "\"";
            int keyIdx = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0)
            {
                return results;
            }

            int arrStart = json.IndexOf('[', keyIdx + token.Length);
            if (arrStart < 0)
            {
                return results;
            }

            int depth = 0;
            int objStart = -1;
            bool inString = false;

            for (int i = arrStart + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inString)
                {
                    i++;
                    continue;
                }
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        objStart = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        results.Add(json.Substring(objStart, i - objStart + 1));
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return results;
        }
    }
}
