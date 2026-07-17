using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Tools
{
    // Parses a slash-command style input into a structured command the app can act on.
    // Supports:  /cmd arg1 arg2   OR   /cmd "quoted arg with spaces" key=value
    // Anything not starting with '/' is returned as NaturalLanguage.
    public class ParsedCommand
    {
        public string Raw;
        public string Verb;
        public List<string> Args = new List<string>();
        public Dictionary<string, string> Flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool IsCommand;
        public string Error;
    }

    public class CommandParser
    {
        static readonly Regex FlagRe = new Regex(@"^(?<k>[A-Za-z_][A-Za-z0-9_]*)=(?<v>.+)$");

        public ParsedCommand Parse(string input)
        {
            var result = new ParsedCommand { Raw = input ?? "" };
            if (string.IsNullOrWhiteSpace(input))
            {
                result.Error = "empty";
                return result;
            }

            string trimmed = input.Trim();
            if (!trimmed.StartsWith("/"))
            {
                result.IsCommand = false;
                return result;
            }

            result.IsCommand = true;
            var tokens = Tokenize(trimmed.Substring(1));
            if (tokens.Count == 0)
            {
                result.Error = "missing verb";
                return result;
            }

            result.Verb = tokens[0].ToLowerInvariant();
            for (int i = 1; i < tokens.Count; i++)
            {
                var t = tokens[i];
                var m = FlagRe.Match(t);
                if (m.Success)
                    result.Flags[m.Groups["k"].Value] = m.Groups["v"].Value;
                else
                    result.Args.Add(t);
            }
            return result;
        }

        // Splits on spaces but keeps "quoted segments" together.
        List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            bool inQuote = false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (char.IsWhiteSpace(c) && !inQuote)
                {
                    if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) tokens.Add(sb.ToString());
            return tokens;
        }
    }
}
