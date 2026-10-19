using Docfx.Plugins; // Ensure that the Docfx.Plugins library is referenced.
using HtmlAgilityPack;
using System.Collections.Immutable;
using System.Composition;
using System.Text.RegularExpressions;

// 1. The class must implement the IPostProcessor interface.
//[Export(nameof(WhatsNew), typeof(IPostProcessor))]
public class WhatsNew : IPostProcessor
{
      

    public ImmutableDictionary<string, object> PrepareMetadata(ImmutableDictionary<string, object> metadata)
    {
        return metadata;
    }

    public Manifest Process(Manifest manifest, string outputFolder, CancellationToken cancellationToken)
    {
        var tagIndex = new Dictionary<string, HashSet<(string Text, string Link)>>();
        var tagPattern = new Regex(@"\bv\d+(?:\.\d+)+\+?\b");

        Console.WriteLine("--- Starting tag analysis ---");

        // --- START OF PATH DEFINITION BLOCK ---
        // Get the path to the folder where the final file will be located.
        // By default, this is outputFolder (_site).
        // We want to place the file in the parent folder.
        string targetDirectory = null;

        try
        {
            // Get the full path to the output folder
            var fullOutputPath = Path.GetFullPath(outputFolder);

            // Check that the path looks like "_site" or "docs\_site"
            if (Path.GetFileName(fullOutputPath).Equals("_site", StringComparison.OrdinalIgnoreCase))
            {
                // If this is the case, then the parent folder is our target
                targetDirectory = Path.GetDirectoryName(fullOutputPath);
                Console.WriteLine($"[DEBUG] Target directory set to parent of '_site': {targetDirectory}");
            }
            else
            {
                // If it’s not "_site" (for example, in a custom build), use outputFolder as is.
                targetDirectory = outputFolder;
                Console.WriteLine($"[DEBUG] Target directory is the outputFolder itself: {targetDirectory}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error determining target directory: {ex.Message}. Using outputFolder as fallback.");
            targetDirectory = outputFolder;
        }
        // --- END OF PATH DEFINITION BLOCK ---


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
                    // Construct the path for a Markdown link (~/path/to/file.md)
                    var relPathToSite = Path.GetRelativePath(outputFolder, filePath);
                    var mdRelPath = Path.ChangeExtension(relPathToSite, ".md");
                    var finalLinkBase = $"~/{mdRelPath.Replace('\\', '/')}";

                    string htmlContent = File.ReadAllText(filePath);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(htmlContent);

                    // We look for headings that contain square brackets within any of their text nodes.
                    var headers = doc.DocumentNode.SelectNodes(
                        "//*[self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6]" +
                        "[.//text()[contains(., '[') and contains(., ']')]]");

                    if (headers == null) continue;

                    foreach (var header in headers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string anchorId = header.GetAttributeValue("id", null);
                        if (string.IsNullOrEmpty(anchorId)) continue;

                        // --- KEY POINT: Parse text nodes, not InnerText ---
                        var textNodes = header.SelectNodes(".//text()");
                        if (textNodes == null) continue;

                        foreach (var textNode in textNodes)
                        {
                            string nodeText = textNode.InnerText;
                            foreach (Match match in tagPattern.Matches(nodeText))
                            {
                                string tag = match.Value; // Take the found tag directly

                                // Create a link, using the ENTIRE header text for display
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


        // --- FILE WRITING ---
        if (tagIndex.Count == 0)
        {
            Console.WriteLine("\n❌ Index is empty. No tags were found. File will not be created.");
            return manifest;
        }

        // We use the targetDirectory variable, which was defined above
        var tocFilePath = Path.Combine(targetDirectory, "tags-reference.md");

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
            Console.WriteLine($"   File location: {tocFilePath}"); // Output the exact path for certainty
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