using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ZhuaQianDesktopApp.Documents;

// Validates OfficeOptimizer trims empty paragraphs from a minimal docx without
// breaking the package. No real model, no pipeline.
static class TestOfficeOptimizer
{
    public static int RunAll()
    {
        int failures = 0;
        failures += TestOptimizeDocxRemovesEmptyParagraphs();
        Console.WriteLine("[TestOfficeOptimizer] failures=" + failures);
        return failures;
    }

    static int TestOptimizeDocxRemovesEmptyParagraphs()
    {
        int fails = 0;
        string dir = Path.Combine(Path.GetTempPath(), "zq_test_opt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "doc.docx");
        try
        {
            File.WriteAllBytes(path, MakeMinimalDocx());
            var res = OfficeOptimizer.OptimizeDocx(path);
            Assert(res.RemovedUnits == 2, "two empty paragraphs removed, got " + res.RemovedUnits);

            using (var ms = new MemoryStream(res.Bytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                var e = zip.GetEntry("word/document.xml");
                using (var r = e.Open())
                using (var rm = new MemoryStream())
                {
                    r.CopyTo(rm);
                    string xml = Encoding.UTF8.GetString(rm.ToArray());
                    Assert(!xml.Contains("<w:p/>"), "empty <w:p/> removed from xml");
                    Assert(xml.Contains("<w:t>Hello</w:t>"), "real content preserved");
                }
            }
        }
        catch (Exception ex) { Assert(false, "no exception: " + ex.Message); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return fails;
    }

    static byte[] MakeMinimalDocx()
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create))
            {
                var doc = zip.CreateEntry("word/document.xml");
                using (var w = doc.Open())
                {
                    string xml = "<?xml version=\"1.0\"?>" +
                        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                        "<w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p><w:p/><w:p/></w:body></w:document>";
                    var bytes = Encoding.UTF8.GetBytes(xml);
                    w.Write(bytes, 0, bytes.Length);
                }
                var ct = zip.CreateEntry("[Content_Types].xml");
                using (var w = ct.Open())
                {
                    string xml = "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                        "<Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>";
                    var bytes = Encoding.UTF8.GetBytes(xml);
                    w.Write(bytes, 0, bytes.Length);
                }
            }
            return ms.ToArray();
        }
    }
}
