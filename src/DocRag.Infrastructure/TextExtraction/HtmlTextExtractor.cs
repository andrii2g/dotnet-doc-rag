using System.Net;
using DocRag.Core.Abstractions;
using DocRag.Core.Documents;
using HtmlAgilityPack;

namespace DocRag.Infrastructure.TextExtraction;

public sealed class HtmlTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = [".html", ".htm"];

    public bool CanExtract(string extension) => SupportedExtensions.Contains(extension);

    public Task<ExtractedDocumentText> ExtractAsync(string managedFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = new HtmlDocument();
        document.Load(managedFilePath);

        RemoveNodes(document, "//script|//style|//nav|//*[@hidden]");
        RemoveNodes(document, "//*[contains(translate(@style,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'display:none')]");

        var text = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        var normalized = PlainTextExtractor.NormalizeText(text);

        return Task.FromResult(new ExtractedDocumentText(
            normalized,
            [new ExtractedTextSection(normalized, null, null, new Dictionary<string, string>())],
            new Dictionary<string, string>()));
    }

    private static void RemoveNodes(HtmlDocument document, string xpath)
    {
        var nodes = document.DocumentNode.SelectNodes(xpath);
        if (nodes is null)
        {
            return;
        }

        foreach (var node in nodes)
        {
            node.Remove();
        }
    }
}
