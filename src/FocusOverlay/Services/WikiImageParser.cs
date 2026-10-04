using System;
using System.IO;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FocusOverlay.Services;

public sealed class WikiImageParser : InlineParser
{
    public WikiImageParser()
    {
        OpeningCharacters = ['!'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.PeekChar(1) != '[' || slice.PeekChar(2) != '[')
        {
            return false;
        }

        var start = slice.Start;
        var contentStart = start + 3;
        if (contentStart > slice.End)
        {
            return false;
        }

        var end = slice.Text.IndexOf("]]", contentStart, slice.End - contentStart + 1, StringComparison.Ordinal);
        if (end <= contentStart)
        {
            return false;
        }

        var content = slice.Text.Substring(contentStart, end - contentStart).Replace("\\|", "|");
        if (content.IndexOfAny(['\n', '\r', '[', ']']) >= 0)
        {
            return false;
        }

        var separator = content.IndexOf('|');
        var target = (separator >= 0 ? content[..separator] : content).Trim();
        if (target.Length == 0)
        {
            return false;
        }

        var label = Path.GetFileNameWithoutExtension(target);

        var position = processor.GetSourcePosition(start, out var line, out var column);
        var link = new LinkInline(target, string.Empty)
        {
            IsImage = true,
            IsClosed = true,
            Span = new SourceSpan(position, position + end + 1 - start),
            Line = line,
            Column = column,
        };
        link.AppendChild(new LiteralInline(label));

        processor.Inline = link;
        slice.Start = end + 2;
        return true;
    }
}
