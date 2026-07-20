using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Documents
{
    // "Optimization" of Office Open XML files (docx/pptx/xlsx). The goal is leaner,
    // cleaner documents without changing structure or breaking package integrity:
    // only the XML *text* inside existing parts is trimmed (empty paragraphs / empty
    // rows removed). No parts are added or removed, so the zip stays valid and opens
    // in Office / LibreOffice.
    //
    // The methods RETURN the optimized file bytes; the caller writes them (typically
    // through WriteFileExecutor's base64 mode) so the write is gated + audited and the
    // original is left untouched (a *_optimized copy is produced). Whole-slide /
    // whole-sheet deletion is intentionally NOT done here: it would require rewriting
    // relationship indices and risks corruption. That is a future, explicitly-audited
    // operation.
    public static class OfficeOptimizer
    {
        public sealed class OptimizeResult
        {
            public int RemovedUnits;
            public string Note = "";
            public byte[] Bytes;
        }

        // Word: drop empty paragraphs and collapse runs of blank paragraphs. Safe.
        public static OptimizeResult OptimizeDocx(string path)
        {
            var res = new OptimizeResult();
            res.Bytes = TransformArchive(File.ReadAllBytes(path),
                name => name == "word/document.xml",
                xml =>
                {
                    int before = CountMatches(xml, "<w:p[ >]");
                    xml = Regex.Replace(xml, "<w:p\\s*/>", "");
                    xml = Regex.Replace(xml, "<w:p>(<w:r>)?</w:p>", "");
                    xml = Regex.Replace(xml, "(<w:p></w:p>){3,}", "<w:p></w:p>");
                    res.RemovedUnits = before - CountMatches(xml, "<w:p[ >]");
                    return xml;
                });
            res.Note = "Removed " + res.RemovedUnits + " empty paragraph(s).";
            return res;
        }

        // PowerPoint: drop empty paragraphs inside each slide's XML. Slides kept.
        public static OptimizeResult OptimizePptx(string path)
        {
            var res = new OptimizeResult();
            res.Bytes = TransformArchive(File.ReadAllBytes(path),
                name => name.StartsWith("ppt/slides/slide") && name.EndsWith(".xml"),
                xml =>
                {
                    int before = CountMatches(xml, "<a:p[ >]");
                    xml = Regex.Replace(xml, "<a:p\\s*/>", "");
                    xml = Regex.Replace(xml, "<a:p></a:p>", "");
                    res.RemovedUnits += before - CountMatches(xml, "<a:p[ >]");
                    return xml;
                });
            res.Note = "Removed " + res.RemovedUnits + " empty paragraph(s) across slides.";
            return res;
        }

        // Excel: drop rows that contain no cells within each worksheet.
        public static OptimizeResult OptimizeXlsx(string path)
        {
            var res = new OptimizeResult();
            res.Bytes = TransformArchive(File.ReadAllBytes(path),
                name => name.StartsWith("xl/worksheets/sheet") && name.EndsWith(".xml"),
                xml =>
                {
                    int before = CountMatches(xml, "<row[ >]");
                    xml = Regex.Replace(xml, "<row[^>]*>\\s*</row>", "");
                    xml = Regex.Replace(xml, "<row\\s*/>", "");
                    res.RemovedUnits += before - CountMatches(xml, "<row[ >]");
                    return xml;
                });
            res.Note = "Removed " + res.RemovedUnits + " empty row(s) across worksheets.";
            return res;
        }

        // Read all parts, apply `transform` to the XML of matching parts, rewrite the
        // archive in memory and return the new bytes.
        static byte[] TransformArchive(byte[] original, Func<string, bool> namePredicate, Func<string, string> transform)
        {
            using (var inStream = new MemoryStream(original))
            using (var inArchive = new ZipArchive(inStream, ZipArchiveMode.Read))
            {
                var entries = new List<KeyValuePair<string, byte[]>>();
                foreach (var entry in inArchive.Entries)
                {
                    using (var es = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        es.CopyTo(ms);
                        byte[] data = ms.ToArray();
                        if (namePredicate(entry.FullName) && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        {
                            string xml = Encoding.UTF8.GetString(data);
                            xml = transform(xml);
                            data = Encoding.UTF8.GetBytes(xml);
                        }
                        entries.Add(new KeyValuePair<string, byte[]>(entry.FullName, data));
                    }
                }
                using (var outStream = new MemoryStream())
                {
                    using (var outArchive = new ZipArchive(outStream, ZipArchiveMode.Create))
                    {
                        foreach (var e in entries)
                        {
                            var ne = outArchive.CreateEntry(e.Key);
                            using (var es = ne.Open())
                                es.Write(e.Value, 0, e.Value.Length);
                        }
                    }
                    return outStream.ToArray();
                }
            }
        }

        static int CountMatches(string text, string pattern)
        {
            return Regex.Matches(text, pattern).Count;
        }
    }
}
