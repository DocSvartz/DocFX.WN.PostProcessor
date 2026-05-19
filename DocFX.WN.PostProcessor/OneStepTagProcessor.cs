using Docfx.Plugins;
using HtmlAgilityPack;
using System.Collections.Immutable;
using System.Composition;
using System.Text;
using System.Text.RegularExpressions;

[Export("ConceptualDocumentProcessor", typeof(IDocumentBuildStep))]
public class TagReferenceBuildStep : IDocumentBuildStep
{
    public string Name => nameof(TagReferenceBuildStep);
    public int BuildOrder => int.MinValue;


    private static readonly Dictionary<string, HashSet<(string Text, string Link)>> TagIndex = new (StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObject = new object();
    private FileModel? FileModel = null;

    private static readonly string _contentType = "conceptual";
    private static readonly string _tagPattern = @"\bv\d+(?:\.\d+)+\+?\b";
    private static readonly string _headerPattern = "//h1 | //h2 | //h3 | //h4 | //h5 | //h6";

    public IEnumerable<FileModel> Prebuild(ImmutableList<FileModel> models, IHostService host)
    {

        var tagrefFile = models.FirstOrDefault( model=>
            Path.GetFileName(model.File).Equals("tags-reference.md", StringComparison.OrdinalIgnoreCase));

        if(tagrefFile != null)
        {
            FileModel = tagrefFile;
            Console.WriteLine($"\nℹ️ INFO: Source file 'tags-reference.md' found from processing.");
        }

        return models;

    }

    public void Build(FileModel model, IHostService host)
    {
        if (model.OriginalFileAndType.Type != DocumentType.Article)
            return;

        var content = (Dictionary<string, object>)model.Content;
        var markdown = (string)content[_contentType];
        var result = host.Markup(markdown, model.OriginalFileAndType, false);

        if (result?.Html != null)
        {
            CollectLinks(result.Html, model);
        }
    }

    public void Postbuild(ImmutableList<FileModel> models, IHostService host)
    {
        if (TagIndex.Count == 0)
            return;

        if(FileModel != null)
        {
            var content = GenerateMarkdownContent();
            object? conceptual;

            if (FileModel.Content is Dictionary<string, object> dict
                && dict.TryGetValue(_contentType, out conceptual) 
                && conceptual is string data)
            {
                dict.Remove(_contentType);
                dict.TryAdd(_contentType, $"{data} {content}");
            }
            else
            {
                FileModel.Content = new Dictionary<string, object>
                {
                    [_contentType] = content,
                    ["title"] = "Tag Reference"
                };
            }
            

            foreach (var item in host.Processor.BuildSteps.Where(x=>x.Name != Name))
            {
                item.Build(FileModel, host);
            }

        }
    }


    private string GenerateMarkdownContent()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Tag Links");

        foreach (var tag in TagIndex.Keys.OrderBy(t => t))
        {
            sb.AppendLine($"## Tag: `{tag}`");
            foreach (var link in TagIndex[tag].OrderBy(l => l.Text))
            {
                sb.AppendLine($"- {link.Link}");
            }
        }

        return sb.ToString();
    }

    private void CollectLinks(string html,FileModel model)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var headers = doc.DocumentNode.SelectNodes(_headerPattern);
        if (headers == null) return;

        foreach (var header in headers)
        {
            var anchorId = header.GetAttributeValue("id", null);
            if (string.IsNullOrEmpty(anchorId)) continue;

            var linkText = header.InnerText.Trim();

            foreach (Match match in Regex.Matches(linkText, _tagPattern))
            {
                var tag = match.Value;
                var fileName = model.File.Split(".")[0];
                var linkMarkdown = $"[{linkText}](/{fileName}#{anchorId})";

                lock (LockObject)
                {
                    if (!TagIndex.TryGetValue(tag, out var linksSet))
                    {
                        linksSet = new HashSet<(string, string)>();
                        TagIndex[tag] = linksSet;
                    }
                    linksSet.Add((linkText, linkMarkdown));
                }
            }
        }
    }
}