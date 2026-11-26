using System.IO.Compression;
using System.Text;
using System.Xml;
namespace PracticeManager.Core;

// Minimal macro-free OPC document, no external relationships, fields or embedded objects.
// Every variable is written as XML text, never markup.
public sealed record ReviewParagraph(string Text,bool Heading=false);
public static class ReviewDocx
{
 private const string W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
 public static byte[] Create(string heading,IReadOnlyList<ReviewParagraph> paragraphs)
 {
  if(paragraphs.Count>1000||heading.Length>150||paragraphs.Sum(p=>(long)p.Text.Length)>400000)throw new ArgumentException("Review document exceeds supported limits.");
  using var stream=new MemoryStream();
  using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){
   void Part(string path,Action<Stream> write){var e=zip.CreateEntry(path,CompressionLevel.Optimal);e.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);using var s=e.Open();write(s);}
   void StaticPart(string path,string xml)=>Part(path,s=>s.Write(Encoding.UTF8.GetBytes(xml)));
   StaticPart("[Content_Types].xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
   StaticPart("_rels/.rels","<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
   Part("word/document.xml",s=>{
    using var x=XmlWriter.Create(s,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false,CheckCharacters=true});
    void Start(string n)=>x.WriteStartElement("w",n,W);
    void Attr(string n,string value)=>x.WriteAttributeString("w",n,W,value);
    void Value(string n,string value){Start(n);Attr("val",value);x.WriteEndElement();}
    void Para(string text,bool head,bool title=false){
     Start("p");Start("pPr");Start("spacing");Attr("after",head?"160":"100");Attr("line","276");Attr("lineRule","auto");x.WriteEndElement();if(head)Value("keepNext","1");x.WriteEndElement();
     Start("r");Start("rPr");Start("rFonts");Attr("ascii","Calibri");Attr("hAnsi","Calibri");x.WriteEndElement();if(head)Value("b","1");Value("color",head?"203B56":"202020");Value("sz",title?"36":head?"26":"22");x.WriteEndElement();
     var lines=text.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
     for(int i=0;i<lines.Length;i++){if(i>0){Start("br");x.WriteEndElement();}Start("t");x.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");x.WriteString(lines[i]);x.WriteEndElement();}
     x.WriteEndElement();x.WriteEndElement();
    }
    x.WriteStartDocument();Start("document");Start("body");Para(heading,true,true);Para("INTERNAL REVIEW COPY — NOT FOR ISSUE",true);
    foreach(var p in paragraphs)Para(p.Text,p.Heading);
    Start("sectPr");Start("pgSz");Attr("w","12240");Attr("h","15840");x.WriteEndElement();Start("pgMar");foreach(var side in new[]{"top","right","bottom","left"})Attr(side,"1080");Attr("header","720");Attr("footer","720");Attr("gutter","0");x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();x.WriteEndDocument();
   });
  }
  var bytes=stream.ToArray();if(bytes.Length>2097152)throw new ArgumentException("Generated document exceeds size limit.");return bytes;
 }
}
