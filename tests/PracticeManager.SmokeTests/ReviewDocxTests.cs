using System.IO.Compression;
using System.Xml.Linq;
using PracticeManager.Core;
internal static class ReviewDocxTests
{
 public static string Text(byte[] data)
 {
  using var m=new MemoryStream(data);using var z=new ZipArchive(m,ZipArchiveMode.Read);using var s=z.GetEntry("word/document.xml")!.Open();
  return string.Join("\n",XDocument.Load(s).Descendants(XName.Get("t","http://schemas.openxmlformats.org/wordprocessingml/2006/main")).Select(x=>x.Value));
 }
 public static void Run(Action<bool,string> check)
 {
  var special="<script>& \"quoted\" Café — 工程";var doc=ReviewDocx.Create("Test",new[]{new ReviewParagraph(special),new ReviewParagraph("second\nline")});
  check(Text(doc).Contains(special)&&Text(doc).Contains("second\nline"),"DOCX user text escaped and Unicode retained");
  check(doc.SequenceEqual(ReviewDocx.Create("Test",new[]{new ReviewParagraph(special),new ReviewParagraph("second\nline")})),"DOCX deterministic within generator runtime");
  using var m=new MemoryStream(doc);using var z=new ZipArchive(m,ZipArchiveMode.Read);
  check(z.Entries.Select(x=>x.FullName).OrderBy(x=>x).SequenceEqual(new[]{"[Content_Types].xml","_rels/.rels","word/document.xml"}.OrderBy(x=>x)),"DOCX contains only minimal safe package parts");
  foreach(var e in z.Entries){using var s=e.Open();var x=XDocument.Load(s);check(!x.Descendants().Any(a=>a.Attribute("TargetMode")?.Value=="External"),"DOCX part contains no external relationships");}
 }
}
