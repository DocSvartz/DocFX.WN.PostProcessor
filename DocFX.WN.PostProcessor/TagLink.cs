namespace DocFX.WN.PostProcessor
{
    public class TagLink : IEquatable<TagLink>
    {
        public string DisplayText { get; }
        public string MarkdownLink { get; }

        public TagLink(string displayText, string markdownLink)
        {
            DisplayText = displayText;
            MarkdownLink = markdownLink;
        }

        public bool Equals(TagLink other)
        {
            if (other is null) return false;
            return DisplayText == other.DisplayText && MarkdownLink == other.MarkdownLink;
        }

        public override bool Equals(object obj) => Equals(obj as TagLink);
        public override int GetHashCode() => HashCode.Combine(DisplayText, MarkdownLink);
    }
}
