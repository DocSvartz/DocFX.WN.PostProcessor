using Docfx.Plugins; // Убедитесь, что у вас подключена библиотека Docfx.Plugins
using HtmlAgilityPack;
using System.Collections.Immutable;
using System.Composition;
using System.Text.RegularExpressions;

// 1. Класс должен реализовывать интерфейс IPostProcessor
[Export(nameof(WhatsNew), typeof(IPostProcessor))]
public class WhatsNew : IPostProcessor
{
      

    public ImmutableDictionary<string, object> PrepareMetadata(ImmutableDictionary<string, object> metadata)
    {
        return metadata;
    }

    public Manifest Process(Manifest manifest, string outputFolder, CancellationToken cancellationToken)
    {
        var tagIndex = new Dictionary<string, HashSet<(string Text, string Link)>>();
        // Финальный паттерн: ищем "v10.0.8" или "v10.0.8+" в тексте
        var tagPattern = new Regex(@"\bv\d+(?:\.\d+)+\+?\b");

        Console.WriteLine("--- Starting tag analysis ---");

        try
        {
            var htmlFiles = Directory.GetFiles(outputFolder, "*.html", SearchOption.AllDirectories);

            if (htmlFiles.Length == 0)
            {
                Console.WriteLine("No HTML files found.");
                return manifest;
            }

            Console.WriteLine($"Found {htmlFiles.Length} HTML files. Processing...");

            foreach (var filePath in htmlFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Формируем путь для ссылки (например, ~/path/to/file.md)
                    var relPathToSite = Path.GetRelativePath(outputFolder, filePath);
                    var mdRelPath = Path.ChangeExtension(relPathToSite, ".md");
                    var finalLinkBase = $"~/{mdRelPath.Replace('\\', '/')}";

                    string htmlContent = File.ReadAllText(filePath);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(htmlContent);

                    // Ищем заголовки, внутри которых ЕСТЬ квадратные скобки в любом из их текстовых узлов.
                    var headers = doc.DocumentNode.SelectNodes(
                        "//*[self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6]" +
                        "[.//text()[contains(., '[') and contains(., ']')]]");

                    if (headers == null) continue;

                    foreach (var header in headers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string anchorId = header.GetAttributeValue("id", null);
                        if (string.IsNullOrEmpty(anchorId)) continue;

                        // --- ФИНАЛЬНОЕ ИСПРАВЛЕНИЕ ---
                        // Проблема: header.InnerText может некорректно склеивать текст из-за вложенных <sup>.
                        // Решение: Ищем теги не в InnerText, а во ВСЕХ текстовых узлах внутри заголовка.
                        var textNodes = header.SelectNodes(".//text()");
                        if (textNodes == null) continue;

                        foreach (var textNode in textNodes)
                        {
                            string nodeText = textNode.InnerText;
                            // Используем Matches, чтобы найти ВСЕ совпадения в этом узле
                            foreach (Match match in tagPattern.Matches(nodeText))
                            {
                                string tag = match.Value; // Берем найденный тег напрямую

                                // Формируем ссылку, используя ВЕСЬ текст заголовка
                                string fullHeaderText = header.InnerText.Trim();
                                var linkMarkdown = $"[{fullHeaderText}]({finalLinkBase}#{anchorId})";

                                tagIndex.TryGetValue(tag, out var linksSet);
                                if (linksSet == null)
                                {
                                    linksSet = new HashSet<(string, string)>();
                                    tagIndex[tag] = linksSet;
                                }
                                linksSet.Add((fullHeaderText, linkMarkdown));
                            }
                        }
                        // --- КОНЕЦ ФИНАЛЬНОГО ИСПРАВЛЕНИЯ ---
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error processing file {filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error accessing files: {ex.Message}");
        }


        // --- ЗАПИСЬ ФАЙЛА ---
        if (tagIndex.Count == 0)
        {
            Console.WriteLine("\n❌ Index is empty. No tags were found. File will not be created.");
            return manifest;
        }

        var tocFilePath = Path.Combine(outputFolder, "tags-reference.md");

        try
        {
            using (var writer = new StreamWriter(tocFilePath))
            {
                writer.WriteLine("# Tag Links\n\n");
                writer.WriteLine("This section collects all places in the documentation marked with tags.\n\n");

                foreach (var tag in tagIndex.Keys.OrderBy(t => t))
                {
                    writer.WriteLine($"## Tag: `{tag}`\n");
                    foreach (var link in tagIndex[tag].OrderBy(l => l.Text))
                    {
                        writer.WriteLine($"- {link.Link}");
                    }
                    writer.WriteLine();
                }
            }

            var fileSizeKb = new FileInfo(tocFilePath).Length / 1024.0;
            Console.WriteLine($"\n🎉 SUCCESS: File 'tags-reference.md' created successfully!");
            Console.WriteLine($"   File size: {fileSizeKb:F2} KB");
            Console.WriteLine($"   Number of tags in TOC: {tagIndex.Count}");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ ERROR writing file: {ex.Message}");
        }

        return manifest;
    }

}