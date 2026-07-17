using System;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Documents
{
    public class Redactor
    {
        public bool Enabled { get { return _Enabled; } set { _Enabled = value; } }
        bool _Enabled = true;

        public Redactor() { }

        public Redactor(bool enabled)
        {
            Enabled = enabled;
        }

        public string Apply(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return text ?? "";

            string value = text;
            value = Regex.Replace(value, "\\b1[3-9]\\d{9}\\b", "[REDACTED_PHONE]");
            value = Regex.Replace(value, "\\b\\d{17}[\\dXx]\\b", "[REDACTED_CN_ID]");
            value = Regex.Replace(value, "\\b(?:\\d[ -]*?){13,19}\\b", "[REDACTED_CARD]");
            value = Regex.Replace(value, "\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", "[REDACTED_EMAIL]", RegexOptions.IgnoreCase);
            return value;
        }

        public string Preview(string text)
        {
            return Apply(text);
        }
    }
}
