using System.Net.Http;
using System.Globalization;

namespace VideoStudio;

// Deno uses its own TLS implementation. It also provides a verified-certificate
// fallback on Windows hosts where Schannel cannot acquire a credential handle.
public static class NetworkFetch
{
    const string Script = """
    const url=Deno.args[0],file=Deno.args[1],limit=Number(Deno.args[2]);
    const response=await fetch(url,{headers:{'User-Agent':'VideoStudio/1.0','Accept':'*/*'}});
    if(!response.ok)throw new Error('HTTP '+response.status);
    if(Number(response.headers.get('content-length'))>limit)throw new Error('Response too large');
    const parts=[];let length=0;
    for await(const chunk of response.body){length+=chunk.length;if(length>limit)throw new Error('Response too large');parts.push(chunk);}
    const bytes=new Uint8Array(length);let offset=0;
    for(const part of parts){bytes.set(part,offset);offset+=part.length;}
    await Deno.writeFile(file,bytes);
    """;

    public static async Task<byte[]> GetAsync(Uri uri, int maximumBytes, string deno, string temporaryDirectory, CancellationToken token)
    {
        if (uri.Scheme is not ("https" or "http")) throw new ArgumentException("Geçersiz indirme adresi.");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VideoStudio/1.0");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidOperationException("İndirilen veri boyut sınırını aşıyor.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > maximumBytes) throw new InvalidOperationException("İndirilen veri boyut sınırını aşıyor.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (HttpRequestException) when (File.Exists(deno)) { }
        Directory.CreateDirectory(temporaryDirectory);
        var temporary = Path.Combine(temporaryDirectory, Guid.NewGuid() + ".download");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var result = await ProcessRunner.RunAsync(deno, ["eval", "--quiet", Script, uri.AbsoluteUri, temporary, maximumBytes.ToString(CultureInfo.InvariantCulture)], timeout.Token);
            if (result.ExitCode != 0 || !File.Exists(temporary)) throw new HttpRequestException("Güvenli bağlantı kurulamadı. " + result.Error.Trim());
            if (new FileInfo(temporary).Length > maximumBytes) throw new InvalidOperationException("İndirilen veri boyut sınırını aşıyor.");
            return await File.ReadAllBytesAsync(temporary, token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
