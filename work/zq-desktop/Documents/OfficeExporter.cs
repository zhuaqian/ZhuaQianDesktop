using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ZhuaQianDesktopApp.Documents
{
    public class OfficeExporter
    {
        public void SaveTxt(string path, string text)
        {
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
        }

        public void SaveMd(string path, string text)
        {
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
        }

        public void SavePng(string path, string text)
        {
            using (var page = RenderTextPage(BuildVisualPages(text, 38, 64)[0], 1, 1))
            {
                page.Save(path, ImageFormat.Png);
            }
        }

        public void SavePdf(string path, string text)
        {
            var pages = BuildVisualPages(text, 38, 64);
            var images = new List<byte[]>();
            try
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    using (var page = RenderTextPage(pages[i], i + 1, pages.Count))
                    using (var ms = new MemoryStream())
                    {
                        page.Save(ms, ImageFormat.Jpeg);
                        images.Add(ms.ToArray());
                    }
                }
                WriteImagePdf(path, images, 595, 842);
            }
            finally
            {
                images.Clear();
            }
        }

        public void SaveDocx(string path, string text)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/></Types>");
                AddEntry(zip, "_rels/.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/></Relationships>");

                var body = new StringBuilder();
                string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                foreach (string raw in lines)
                {
                    body.Append("<w:p><w:r><w:t xml:space=\"preserve\">");
                    body.Append(XmlEscape(raw ?? ""));
                    body.Append("</w:t></w:r></w:p>");
                }
                AddEntry(zip, "word/document.xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main""><w:body>"
                    + body.ToString()
                    + @"<w:sectPr><w:pgSz w:w=""11906"" w:h=""16838""/><w:pgMar w:top=""1440"" w:right=""1440"" w:bottom=""1440"" w:left=""1440""/></w:sectPr></w:body></w:document>");
            }
        }

        public void SavePptx(string path, string text)
        {
            var slides = BuildSlides(text);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", PptContentTypes(slides.Count));
                AddEntry(zip, "_rels/.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""ppt/presentation.xml""/></Relationships>");
                AddEntry(zip, "ppt/presentation.xml", PptPresentationXml(slides.Count));
                AddEntry(zip, "ppt/_rels/presentation.xml.rels", PptPresentationRels(slides.Count));
                AddEntry(zip, "ppt/theme/theme1.xml", PptThemeXml());
                AddEntry(zip, "ppt/slideMasters/slideMaster1.xml", PptMasterXml());
                AddEntry(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout"" Target=""../slideLayouts/slideLayout1.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme"" Target=""../theme/theme1.xml""/></Relationships>");
                AddEntry(zip, "ppt/slideLayouts/slideLayout1.xml", PptLayoutXml());
                AddEntry(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster"" Target=""../slideMasters/slideMaster1.xml""/></Relationships>");

                for (int i = 0; i < slides.Count; i++)
                {
                    AddEntry(zip, "ppt/slides/slide" + (i + 1).ToString() + ".xml", PptSlideXml(slides[i]));
                    AddEntry(zip, "ppt/slides/_rels/slide" + (i + 1).ToString() + ".xml.rels",
                        @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout"" Target=""../slideLayouts/slideLayout1.xml""/></Relationships>");
                }
            }
        }

        public void SaveXlsx(string path, string text)
        {
            var rows = BuildRows(text);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/><Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/><Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/></Types>");
                AddEntry(zip, "_rels/.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/></Relationships>");
                AddEntry(zip, "xl/workbook.xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""><sheets><sheet name=""ZhuaQian"" sheetId=""1"" r:id=""rId1""/></sheets></workbook>");
                AddEntry(zip, "xl/_rels/workbook.xml.rels",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/><Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/></Relationships>");
                AddEntry(zip, "xl/styles.xml",
                    @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><fonts count=""1""><font><sz val=""11""/><name val=""Calibri""/></font></fonts><fills count=""1""><fill><patternFill patternType=""none""/></fill></fills><borders count=""1""><border/></borders><cellStyleXfs count=""1""><xf/></cellStyleXfs><cellXfs count=""1""><xf xfId=""0""/></cellXfs></styleSheet>");

                var sheet = new StringBuilder();
                sheet.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
                for (int r = 0; r < rows.Count; r++)
                {
                    sheet.Append("<row r=\"").Append(r + 1).Append("\">");
                    var row = rows[r];
                    for (int c = 0; c < row.Count; c++)
                    {
                        string cellRef = ColumnName(c + 1) + (r + 1).ToString();
                        sheet.Append("<c r=\"").Append(cellRef).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                        sheet.Append(XmlEscape(row[c]));
                        sheet.Append("</t></is></c>");
                    }
                    sheet.Append("</row>");
                }
                sheet.Append("</sheetData></worksheet>");
                AddEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            }
        }

        void AddEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using (var s = entry.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false)))
                w.Write(content);
        }

        List<List<string>> BuildVisualPages(string text, int linesPerPage, int charsPerLine)
        {
            var pages = new List<List<string>>();
            var current = new List<string>();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                foreach (string wrapped in WrapVisualLine(raw ?? "", charsPerLine))
                {
                    current.Add(wrapped);
                    if (current.Count >= linesPerPage)
                    {
                        pages.Add(current);
                        current = new List<string>();
                    }
                }
            }
            if (current.Count > 0) pages.Add(current);
            if (pages.Count == 0) pages.Add(new List<string> { "ZhuaQian" });
            return pages;
        }

        IEnumerable<string> WrapVisualLine(string line, int charsPerLine)
        {
            string value = line ?? "";
            if (value.Length == 0)
            {
                yield return "";
                yield break;
            }
            int index = 0;
            while (index < value.Length)
            {
                int take = Math.Min(charsPerLine, value.Length - index);
                yield return value.Substring(index, take);
                index += take;
            }
        }

        Bitmap RenderTextPage(List<string> lines, int pageNumber, int pageCount)
        {
            const int width = 1240;
            const int height = 1754;
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            using (var titleFont = new Font("Microsoft YaHei UI", 30, FontStyle.Bold))
            using (var bodyFont = new Font("Microsoft YaHei UI", 24, FontStyle.Regular))
            using (var footerFont = new Font("Microsoft YaHei UI", 16, FontStyle.Regular))
            using (var ink = new SolidBrush(Color.FromArgb(24, 29, 39)))
            using (var muted = new SolidBrush(Color.FromArgb(100, 116, 139)))
            using (var linePen = new Pen(Color.FromArgb(226, 232, 240), 2))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.DrawString("ZhuaQian Office Output", titleFont, ink, new RectangleF(80, 62, width - 160, 54));
                g.DrawLine(linePen, 80, 140, width - 80, 140);
                float y = 178;
                foreach (string line in lines)
                {
                    string value = line ?? "";
                    if (value.StartsWith("#"))
                    {
                        g.DrawString(Regex.Replace(value, @"^#+\s*", ""), titleFont, ink, new RectangleF(80, y, width - 160, 50));
                        y += 58;
                    }
                    else
                    {
                        g.DrawString(value, bodyFont, ink, new RectangleF(80, y, width - 160, 44));
                        y += 40;
                    }
                    if (y > height - 130) break;
                }
                string footer = "Page " + pageNumber.ToString() + " / " + pageCount.ToString();
                g.DrawLine(linePen, 80, height - 92, width - 80, height - 92);
                g.DrawString(footer, footerFont, muted, new RectangleF(80, height - 72, width - 160, 28));
            }
            return bmp;
        }

        void WriteImagePdf(string path, List<byte[]> jpegPages, int pageWidth, int pageHeight)
        {
            var objects = new List<byte[]>();
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            var kids = new StringBuilder();
            for (int i = 0; i < jpegPages.Count; i++) kids.Append(3 + i * 3).Append(" 0 R ");
            objects.Add(Ascii("<< /Type /Pages /Count " + jpegPages.Count + " /Kids [ " + kids.ToString() + "] >>"));

            for (int i = 0; i < jpegPages.Count; i++)
            {
                int pageObj = 3 + i * 3;
                int imageObj = pageObj + 1;
                int contentObj = pageObj + 2;
                objects.Add(Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pageWidth + " " + pageHeight + "] /Resources << /XObject << /Im" + (i + 1).ToString() + " " + imageObj + " 0 R >> >> /Contents " + contentObj + " 0 R >>"));
                objects.Add(PdfStream("<< /Type /XObject /Subtype /Image /Width 1240 /Height 1754 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpegPages[i].Length + " >>", jpegPages[i]));
                string content = "q\n" + pageWidth + " 0 0 " + pageHeight + " 0 0 cm\n/Im" + (i + 1).ToString() + " Do\nQ\n";
                objects.Add(PdfStream("<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>", Ascii(content)));
            }

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                WriteAscii(fs, "%PDF-1.4\n");
                var offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(fs.Position);
                    WriteAscii(fs, (i + 1).ToString() + " 0 obj\n");
                    fs.Write(objects[i], 0, objects[i].Length);
                    WriteAscii(fs, "\nendobj\n");
                }
                long xref = fs.Position;
                WriteAscii(fs, "xref\n0 " + (objects.Count + 1).ToString() + "\n0000000000 65535 f \n");
                foreach (long offset in offsets) WriteAscii(fs, offset.ToString("0000000000") + " 00000 n \n");
                WriteAscii(fs, "trailer\n<< /Size " + (objects.Count + 1).ToString() + " /Root 1 0 R >>\nstartxref\n" + xref.ToString() + "\n%%EOF");
            }
        }

        byte[] PdfStream(string header, byte[] body)
        {
            using (var ms = new MemoryStream())
            {
                byte[] start = Ascii(header + "\nstream\n");
                byte[] end = Ascii("\nendstream");
                ms.Write(start, 0, start.Length);
                ms.Write(body, 0, body.Length);
                ms.Write(end, 0, end.Length);
                return ms.ToArray();
            }
        }

        byte[] Ascii(string value)
        {
            return Encoding.ASCII.GetBytes(value ?? "");
        }

        void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Ascii(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        string XmlEscape(string value)
        {
            return System.Security.SecurityElement.Escape(value ?? "") ?? "";
        }

        List<List<string>> BuildSlides(string text)
        {
            var slides = new List<List<string>>();
            var current = new List<string>();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                bool startsHeading = line.StartsWith("#") || line.StartsWith("Slide ", StringComparison.OrdinalIgnoreCase);
                if ((startsHeading && current.Count > 0) || current.Count >= 8)
                {
                    slides.Add(current);
                    current = new List<string>();
                }
                current.Add(Regex.Replace(line, @"^#+\s*", ""));
            }
            if (current.Count > 0) slides.Add(current);
            if (slides.Count == 0) slides.Add(new List<string> { "ZhuaQian" });
            return slides;
        }

        string PptContentTypes(int count)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types""><Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/><Default Extension=""xml"" ContentType=""application/xml""/><Override PartName=""/ppt/presentation.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml""/><Override PartName=""/ppt/slideMasters/slideMaster1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml""/><Override PartName=""/ppt/slideLayouts/slideLayout1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml""/><Override PartName=""/ppt/theme/theme1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.theme+xml""/>");
            for (int i = 1; i <= count; i++)
                sb.Append("<Override PartName=\"/ppt/slides/slide").Append(i).Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        string PptPresentationXml(int count)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><p:presentation xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main""><p:sldMasterIdLst><p:sldMasterId id=""2147483648"" r:id=""rId1""/></p:sldMasterIdLst><p:sldIdLst>");
            for (int i = 1; i <= count; i++)
                sb.Append("<p:sldId id=\"").Append(255 + i).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
            sb.Append(@"</p:sldIdLst><p:sldSz cx=""12192000"" cy=""6858000"" type=""screen16x9""/><p:notesSz cx=""6858000"" cy=""9144000""/></p:presentation>");
            return sb.ToString();
        }

        string PptPresentationRels(int count)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships""><Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster"" Target=""slideMasters/slideMaster1.xml""/>");
            for (int i = 1; i <= count; i++)
                sb.Append("<Relationship Id=\"rId").Append(i + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide").Append(i).Append(".xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        string PptSlideXml(List<string> lines)
        {
            string title = lines.Count > 0 ? lines[0] : "ZhuaQian";
            var body = new StringBuilder();
            for (int i = 1; i < lines.Count; i++)
            {
                body.Append(@"<a:p><a:r><a:rPr lang=""zh-CN"" sz=""2100""/><a:t>");
                body.Append(XmlEscape(lines[i]));
                body.Append("</a:t></a:r></a:p>");
            }
            if (body.Length == 0) body.Append("<a:p/>");
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><p:sld xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main""><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=""0"" y=""0""/><a:ext cx=""0"" cy=""0""/><a:chOff x=""0"" y=""0""/><a:chExt cx=""0"" cy=""0""/></a:xfrm></p:grpSpPr><p:sp><p:nvSpPr><p:cNvPr id=""2"" name=""Title""/><p:cNvSpPr txBox=""1""/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=""685800"" y=""457200""/><a:ext cx=""10820400"" cy=""914400""/></a:xfrm><a:prstGeom prst=""rect""><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=""zh-CN"" sz=""3400"" b=""1""/><a:t>"
                + XmlEscape(title)
                + @"</a:t></a:r></a:p></p:txBody></p:sp><p:sp><p:nvSpPr><p:cNvPr id=""3"" name=""Content""/><p:cNvSpPr txBox=""1""/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=""914400"" y=""1676400""/><a:ext cx=""10363200"" cy=""4419600""/></a:xfrm><a:prstGeom prst=""rect""><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/>"
                + body.ToString()
                + @"</p:txBody></p:sp></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
        }

        string PptMasterXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><p:sldMaster xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main""><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=""0"" y=""0""/><a:ext cx=""0"" cy=""0""/><a:chOff x=""0"" y=""0""/><a:chExt cx=""0"" cy=""0""/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMap bg1=""lt1"" tx1=""dk1"" bg2=""lt2"" tx2=""dk2"" accent1=""accent1"" accent2=""accent2"" accent3=""accent3"" accent4=""accent4"" accent5=""accent5"" accent6=""accent6"" hlink=""hlink"" folHlink=""folHlink""/><p:sldLayoutIdLst><p:sldLayoutId id=""2147483649"" r:id=""rId1""/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>";
        }

        string PptLayoutXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><p:sldLayout xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"" type=""titleAndBody"" preserve=""1""><p:cSld name=""Title and Content""><p:spTree><p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=""0"" y=""0""/><a:ext cx=""0"" cy=""0""/><a:chOff x=""0"" y=""0""/><a:chExt cx=""0"" cy=""0""/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
        }

        string PptThemeXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><a:theme xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" name=""ZhuaQian""><a:themeElements><a:clrScheme name=""ZhuaQian""><a:dk1><a:srgbClr val=""111111""/></a:dk1><a:lt1><a:srgbClr val=""FFFFFF""/></a:lt1><a:dk2><a:srgbClr val=""1F2937""/></a:dk2><a:lt2><a:srgbClr val=""F8FAFC""/></a:lt2><a:accent1><a:srgbClr val=""2563EB""/></a:accent1><a:accent2><a:srgbClr val=""059669""/></a:accent2><a:accent3><a:srgbClr val=""DC2626""/></a:accent3><a:accent4><a:srgbClr val=""7C3AED""/></a:accent4><a:accent5><a:srgbClr val=""EA580C""/></a:accent5><a:accent6><a:srgbClr val=""0891B2""/></a:accent6><a:hlink><a:srgbClr val=""2563EB""/></a:hlink><a:folHlink><a:srgbClr val=""7C3AED""/></a:folHlink></a:clrScheme><a:fontScheme name=""ZhuaQian""><a:majorFont><a:latin typeface=""Microsoft YaHei""/></a:majorFont><a:minorFont><a:latin typeface=""Microsoft YaHei""/></a:minorFont></a:fontScheme><a:fmtScheme name=""ZhuaQian""><a:fillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w=""9525""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
        }

        List<List<string>> BuildRows(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var tableRows = new List<List<string>>();
            foreach (string line in lines)
            {
                string trimmed = (line ?? "").Trim();
                if (trimmed.Length == 0 || !trimmed.Contains("|")) continue;
                if (Regex.IsMatch(trimmed.Trim('|', ' '), @"^[-:\s|]+$")) continue;
                string clean = trimmed.Trim();
                if (clean.StartsWith("|")) clean = clean.Substring(1);
                if (clean.EndsWith("|")) clean = clean.Substring(0, clean.Length - 1);
                var row = new List<string>();
                foreach (string cell in clean.Split('|')) row.Add(cell.Trim());
                if (row.Count > 1) tableRows.Add(row);
            }
            if (tableRows.Count > 0) return tableRows;

            var csvRows = new List<List<string>>();
            foreach (string line in lines)
            {
                string trimmed = (line ?? "").Trim();
                if (trimmed.Length == 0 || !trimmed.Contains(",")) continue;
                var row = ParseCsvLine(trimmed);
                if (row.Count > 1) csvRows.Add(row);
            }
            if (csvRows.Count > 0) return csvRows;

            var rows = new List<List<string>>();
            foreach (string line in lines)
            {
                string trimmed = (line ?? "").Trim();
                if (trimmed.Length == 0) continue;
                rows.Add(new List<string> { trimmed });
            }
            if (rows.Count == 0) rows.Add(new List<string> { text ?? "" });
            return rows;
        }

        string ColumnName(int index)
        {
            var sb = new StringBuilder();
            while (index > 0)
            {
                index--;
                sb.Insert(0, (char)('A' + (index % 26)));
                index /= 26;
            }
            return sb.ToString();
        }

        List<string> ParseCsvLine(string line)
        {
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }
                }
                else
                {
                    if (ch == '"') quoted = true;
                    else if (ch == ',')
                    {
                        row.Add(cell.ToString().Trim());
                        cell.Length = 0;
                    }
                    else cell.Append(ch);
                }
            }
            row.Add(cell.ToString().Trim());
            return row;
        }
    }
}
