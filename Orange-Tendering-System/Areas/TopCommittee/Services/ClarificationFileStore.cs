using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
namespace Orange_Tendering_System.Areas.TopCommittee.Services;
public sealed class ClarificationFileStore(string contentRoot)
{
    public const long MaxBytes=10*1024*1024;
    public string Root => Path.GetFullPath(Path.Combine(contentRoot,"Areas","TopCommittee","App_Data","ClarificationAttachments"));
    public string PathFor(string key)
    {
        if (Path.GetFileName(key)!=key || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(key),"N",out _)) throw new InvalidOperationException("Invalid attachment reference.");
        var path=Path.GetFullPath(Path.Combine(Root,key));
        if(!path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid attachment reference.");
        return path;
    }
    public async Task<StoredClarificationFile?> Save(IFormFile? upload,CancellationToken token)
    {
        if(upload is null) return null;
        if(upload.Length==0) throw new InvalidOperationException("The selected attachment is empty.");
        if(upload.Length>MaxBytes) throw new InvalidOperationException("The attachment must be 10 MB or smaller.");
        var name=Path.GetFileName(upload.FileName.Replace('\\','/'));
        name=new string(name.Where(c=>!char.IsControl(c)&&!Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        if(string.IsNullOrWhiteSpace(name) || name.Length>240) throw new InvalidOperationException("Choose a file with a valid filename (up to 240 characters).");
        var extension=Path.GetExtension(name).ToLowerInvariant();
        if(extension is not (".pdf" or ".docx" or ".jpg" or ".jpeg" or ".png")) throw new InvalidOperationException("Allowed attachments: PDF, DOCX, JPG and PNG.");
        using var data=new MemoryStream(); await using(var source=upload.OpenReadStream())
        {
            var buffer=new byte[65536]; int count;
            while((count=await source.ReadAsync(buffer,token))>0) { if(data.Length+count>MaxBytes) throw new InvalidOperationException("The attachment must be 10 MB or smaller."); await data.WriteAsync(buffer.AsMemory(0,count),token); }
        }
        var bytes=data.ToArray(); if(bytes.LongLength!=upload.Length) throw new InvalidOperationException("The upload is incomplete. Please select the file again.");
        var valid=extension switch { ".pdf"=>Pdf(bytes), ".docx"=>Docx(bytes), ".png"=>Png(bytes), _=>Jpeg(bytes) };
        if(!valid) throw new InvalidOperationException("The file contents do not match a valid PDF, DOCX, JPG or PNG file.");
        var mime=extension switch { ".pdf"=>"application/pdf", ".docx"=>"application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".png"=>"image/png", _=>"image/jpeg" };
        var key=Guid.NewGuid().ToString("N")+extension; Directory.CreateDirectory(Root);
        try { await using var stream=new FileStream(PathFor(key),FileMode.CreateNew,FileAccess.Write,FileShare.None); await stream.WriteAsync(bytes,token); }
        catch { Remove(key); throw; }
        return new(name,key,mime,bytes.LongLength);
    }
    public void Remove(string key) { var path=PathFor(key); if(File.Exists(path)) File.Delete(path); }
    private static bool Pdf(byte[] b)
    {
        if(b.Length<32 || !b.AsSpan(0,5).SequenceEqual("%PDF-"u8)) return false;
        var tail=System.Text.Encoding.ASCII.GetString(b,Math.Max(0,b.Length-2048),Math.Min(2048,b.Length));
        var match=System.Text.RegularExpressions.Regex.Match(tail,@"startxref\s+(\d+)\s+%%EOF\s*$");
        if(!match.Success || !long.TryParse(match.Groups[1].Value,out var offset) || offset<0 || offset>b.Length-4) return false;
        var crossReference=System.Text.Encoding.ASCII.GetString(b,(int)offset,Math.Min(4096,b.Length-(int)offset));
        return crossReference.StartsWith("xref",StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(crossReference,@"^\d+\s+\d+\s+obj[\s\S]*?/Type\s*/XRef\b");
    }
    private static bool Docx(byte[] bytes)
    {
        try
        {
            using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
            if(zip.Entries.Count>1000 || zip.Entries.Sum(e=>e.Length)>40*1024*1024 || zip.Entries.Any(e=>e.FullName.EndsWith("vbaProject.bin",StringComparison.OrdinalIgnoreCase))) return false;
            XDocument Read(ZipArchiveEntry? entry) { if(entry is null || entry.Length>8*1024*1024) throw new InvalidDataException(); using var s=entry.Open(); using var reader=XmlReader.Create(s,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024 }); return XDocument.Load(reader); }
            var types=Read(zip.GetEntry("[Content_Types].xml"));
            if(!types.Descendants().Any(e=>e.Name.LocalName=="Override" && (string?)e.Attribute("PartName")=="/word/document.xml" && (string?)e.Attribute("ContentType")=="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")) return false;
            return Read(zip.GetEntry("word/document.xml")).Root?.Name == XName.Get("document","http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        }
        catch(Exception e) when(e is InvalidDataException or XmlException or IOException or ArgumentException or OverflowException) { return false; }
    }
    private static bool Png(byte[] b)
    {
        if(b.Length<45 || !b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return false;
        var offset=8; var image=false; var header=false;
        while(offset+12<=b.Length)
        {
            var length=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset,4)); if(length>MaxBytes || offset+12L+length>b.Length) return false;
            var n=(int)length; var type=System.Text.Encoding.ASCII.GetString(b,offset+4,4);
            if(!header) { if(type!="IHDR" || n!=13 || BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset+8,4))==0 || BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset+12,4))==0) return false; header=true; }
            uint crc=0xffffffff;
            for(var i=offset+4;i<offset+8+n;i++) { crc^=b[i]; for(var bit=0;bit<8;bit++) crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1; }
            if((crc^0xffffffff)!=BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(offset+8+n,4))) return false;
            if(type=="IDAT" && n>0) image=true;
            offset+=n+12;
            if(type=="IEND") return n==0 && image && offset==b.Length;
        }
        return false;
    }
    private static bool Jpeg(byte[] b)
    {
        if(b.Length<12 || b[0]!=255 || b[1]!=216 || b[^2]!=255 || b[^1]!=217) return false;
        var offset=2; var frame=false;
        while(offset+4<=b.Length)
        {
            if(b[offset++]!=255) return false; while(offset<b.Length && b[offset]==255) offset++;
            if(offset>=b.Length) return false; var marker=b[offset++];
            if(marker==218) return frame && offset<b.Length-2;
            if(marker==217) return false;
            var length=BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset,2)); if(length<2 || offset+length>b.Length) return false;
            if(marker is 192 or 193 or 194) { if(length<8 || BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset+3,2))==0 || BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(offset+5,2))==0) return false; frame=true; }
            offset+=length;
        }
        return false;
    }
}
