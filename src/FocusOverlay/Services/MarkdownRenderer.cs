using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Renderers.Html;
using Md = Markdig.Syntax;
using MdInlines = Markdig.Syntax.Inlines;
using MdTables = Markdig.Extensions.Tables;
using MdTaskLists = Markdig.Extensions.TaskLists;

namespace FocusOverlay.Services;

public sealed record MarkdownTheme(
    double FontSize,
    Brush Foreground,
    Brush Accent,
    Brush Muted,
    Brush CodeBackground,
    Brush Highlight,
    double ContentWidth,
    Func<string, Uri?> ResolveImage);

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseGenericAttributes()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static readonly FontFamily _textFont = new("Segoe UI");
    private static readonly FontFamily _codeFont = new("Consolas");
    private static readonly ConcurrentDictionary<string, (DateTime Stamp, BitmapSource Image)> _imageCache = new();

    public static FlowDocument Render(string markdown, MarkdownTheme theme)
    {
        var document = new FlowDocument
        {
            FontFamily = _textFont,
            FontSize = theme.FontSize,
            Foreground = theme.Foreground,
            Background = Brushes.Transparent,
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };

        AddBlocks(Markdown.Parse(markdown ?? string.Empty, _pipeline), document.Blocks, theme);
        return document;
    }

    private static void AddBlocks(Md.ContainerBlock container, BlockCollection target, MarkdownTheme theme)
    {
        foreach (var block in container)
        {
            var rendered = RenderBlock(block, theme);
            if (rendered != null)
            {
                target.Add(rendered);
            }
        }
    }

    private static Block? RenderBlock(Md.Block block, MarkdownTheme theme) => block switch
    {
        Md.HeadingBlock heading => RenderHeading(heading, theme),
        Md.ParagraphBlock paragraph => RenderParagraph(paragraph.Inline, theme),
        Md.ListBlock list => RenderList(list, theme),
        Md.QuoteBlock quote => RenderQuote(quote, theme),
        Md.CodeBlock code => RenderCode(code, theme),
        Md.ThematicBreakBlock => RenderRule(theme),
        MdTables.Table table => RenderTable(table, theme),
        Md.LinkReferenceDefinitionGroup => null,
        Md.HtmlBlock => null,
        Md.ContainerBlock container => RenderSection(container, theme),
        Md.LeafBlock leaf => RenderParagraph(leaf.Inline, theme),
        _ => null,
    };

    private static Paragraph RenderHeading(Md.HeadingBlock heading, MarkdownTheme theme)
    {
        var scale = heading.Level switch
        {
            1 => 1.55,
            2 => 1.3,
            3 => 1.15,
            _ => 1.0,
        };

        var paragraph = RenderParagraph(heading.Inline, theme);
        paragraph.FontSize = theme.FontSize * scale;
        paragraph.FontWeight = FontWeights.Bold;
        paragraph.Margin = new Thickness(0, theme.FontSize * 0.4, 0, theme.FontSize * 0.3);

        if (heading.Level <= 2)
        {
            paragraph.Foreground = theme.Accent;
        }

        if (heading.Level == 1)
        {
            paragraph.BorderBrush = theme.Muted;
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.Padding = new Thickness(0, 0, 0, theme.FontSize * 0.15);
        }

        return paragraph;
    }

    private static Paragraph RenderParagraph(MdInlines.ContainerInline? inline, MarkdownTheme theme)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, theme.FontSize * 0.5) };
        if (inline != null)
        {
            AddInlines(inline, paragraph.Inlines, theme);
        }

        return paragraph;
    }

    private static List RenderList(Md.ListBlock source, MarkdownTheme theme)
    {
        var hasTasks = source.OfType<Md.ListItemBlock>().Any(IsTaskItem);
        var list = new List
        {
            MarkerStyle = hasTasks ? TextMarkerStyle.None : source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, theme.FontSize * 0.5),
            Padding = new Thickness(hasTasks ? theme.FontSize * 0.2 : theme.FontSize * 1.3, 0, 0, 0),
        };

        if (source.IsOrdered && int.TryParse(source.OrderedStart, out var start))
        {
            list.StartIndex = start;
        }

        foreach (var child in source)
        {
            if (child is not Md.ListItemBlock item)
            {
                continue;
            }

            var listItem = new ListItem();
            AddBlocks(item, listItem.Blocks, theme);
            foreach (var paragraph in listItem.Blocks.OfType<Paragraph>())
            {
                paragraph.Margin = new Thickness(0, 0, 0, theme.FontSize * 0.15);
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static bool IsTaskItem(Md.ListItemBlock item) =>
        item.FirstOrDefault() is Md.ParagraphBlock { Inline.FirstChild: MdTaskLists.TaskList };

    private static Section RenderQuote(Md.QuoteBlock quote, MarkdownTheme theme)
    {
        var section = new Section
        {
            BorderBrush = theme.Accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(theme.FontSize * 0.6, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, theme.FontSize * 0.5),
            Foreground = theme.Muted,
            FontStyle = FontStyles.Italic,
        };
        AddBlocks(quote, section.Blocks, theme);
        return section;
    }

    private static Paragraph RenderCode(Md.CodeBlock code, MarkdownTheme theme)
    {
        var lines = code.Lines.Lines.Take(code.Lines.Count).Select(line => line.Slice.ToString());
        return new Paragraph(new Run(string.Join("\n", lines)))
        {
            FontFamily = _codeFont,
            FontSize = theme.FontSize * 0.9,
            Background = theme.CodeBackground,
            Padding = new Thickness(theme.FontSize * 0.5),
            Margin = new Thickness(0, 0, 0, theme.FontSize * 0.5),
        };
    }

    private static Paragraph RenderRule(MarkdownTheme theme) => new()
    {
        Margin = new Thickness(0, theme.FontSize * 0.2, 0, theme.FontSize * 0.6),
        BorderBrush = theme.Muted,
        BorderThickness = new Thickness(0, 0, 0, 1),
        FontSize = 1,
        LineHeight = 1,
    };

    private static Section RenderSection(Md.ContainerBlock container, MarkdownTheme theme)
    {
        var section = new Section();
        AddBlocks(container, section.Blocks, theme);
        return section;
    }

    private static Table RenderTable(MdTables.Table source, MarkdownTheme theme)
    {
        var attributes = source.TryGetAttributes();
        var borders = ParseBorders(ReadAttribute(attributes, "borders") ?? ReadAttribute(attributes, "border"));
        var widths = (ReadAttribute(attributes, "widths") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries);

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, theme.FontSize * 0.5),
            BorderBrush = theme.Muted,
            BorderThickness = borders is TableBorders.All or TableBorders.Outer ? new Thickness(0.5) : new Thickness(0),
        };

        var rows = source.OfType<MdTables.TableRow>().ToList();
        var columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        var parsedWidths = Enumerable.Range(0, columnCount).Select(i => ParseColumnWidth(i < widths.Length ? widths[i] : null)).ToList();
        var givenStars = parsedWidths.Where(width => width is { IsStar: true }).Select(width => width!.Value.Value).ToList();
        var fallbackWidth = new GridLength(givenStars.Count > 0 ? givenStars.Average() : 1, GridUnitType.Star);
        var columnWidths = ResolveColumnWidths(
            parsedWidths.Select(width => width ?? fallbackWidth).ToList(),
            theme.ContentWidth);
        foreach (var width in columnWidths)
        {
            table.Columns.Add(new TableColumn { Width = width });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cellBorder = borders switch
            {
                TableBorders.All => new Thickness(0.5),
                TableBorders.Rows when rowIndex < rows.Count - 1 => new Thickness(0, 0, 0, 0.5),
                _ => new Thickness(0),
            };
            var tableRow = new TableRow();
            if (row.IsHeader)
            {
                tableRow.FontWeight = FontWeights.Bold;
                tableRow.Foreground = theme.Accent;
                tableRow.Background = theme.CodeBackground;
            }

            foreach (var cell in row.OfType<MdTables.TableCell>())
            {
                var tableCell = new TableCell
                {
                    BorderBrush = theme.Muted,
                    BorderThickness = cellBorder,
                    Padding = new Thickness(theme.FontSize * 0.35, theme.FontSize * 0.15, theme.FontSize * 0.35, theme.FontSize * 0.15),
                    ColumnSpan = Math.Max(1, cell.ColumnSpan),
                    TextAlignment = GetAlignment(source, cell.ColumnIndex),
                };

                AddBlocks(cell, tableCell.Blocks, theme);
                foreach (var paragraph in tableCell.Blocks.OfType<Paragraph>())
                {
                    paragraph.Margin = new Thickness(0);
                }

                tableRow.Cells.Add(tableCell);
            }

            group.Rows.Add(tableRow);
        }

        return table;
    }

    private static TableBorders ParseBorders(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" or "0" or "false" => TableBorders.None,
        "rows" => TableBorders.Rows,
        "outer" => TableBorders.Outer,
        _ => TableBorders.All,
    };

    private static GridLength? ParseColumnWidth(string? value)
    {
        var text = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text == "*")
        {
            return new GridLength(1, GridUnitType.Star);
        }

        if (text.EndsWith('*') || text.EndsWith('%'))
        {
            return TryParseNumber(text[..^1], out var weight) ? new GridLength(weight, GridUnitType.Star) : null;
        }

        return TryParseNumber(text.Replace("px", string.Empty), out var pixels) ? new GridLength(pixels, GridUnitType.Pixel) : null;
    }

    private static System.Collections.Generic.List<GridLength> ResolveColumnWidths(System.Collections.Generic.List<GridLength> widths, double availableWidth)
    {
        if (widths.All(width => width.IsAbsolute))
        {
            return widths;
        }

        const double minStarWidth = 24;
        var starWeights = widths.Where(width => width.IsStar).Sum(width => width.Value);
        var remaining = Math.Max(0, availableWidth - widths.Where(width => width.IsAbsolute).Sum(width => width.Value));
        var pixels = widths
            .Select(width => width.IsStar ? Math.Max(minStarWidth, remaining * width.Value / starWeights) : width.Value)
            .ToList();
        var total = pixels.Sum();

        return pixels
            .Select(value => new GridLength(value * widths.Count / total, GridUnitType.Star))
            .ToList();
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static TextAlignment GetAlignment(MdTables.Table table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[columnIndex].Alignment switch
        {
            MdTables.TableColumnAlign.Center => TextAlignment.Center,
            MdTables.TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    private static void AddInlines(MdInlines.ContainerInline container, InlineCollection target, MarkdownTheme theme)
    {
        foreach (var inline in container)
        {
            var rendered = RenderInline(inline, theme);
            if (rendered != null)
            {
                target.Add(rendered);
            }
        }
    }

    private static Inline? RenderInline(MdInlines.Inline inline, MarkdownTheme theme) => inline switch
    {
        MdInlines.LiteralInline literal => new Run(literal.Content.ToString()),
        MdInlines.CodeInline code => new Run(code.Content)
        {
            FontFamily = _codeFont,
            Background = theme.CodeBackground,
        },
        MdInlines.LineBreakInline lineBreak => lineBreak.IsHard ? new LineBreak() : new Run(" "),
        MdInlines.HtmlEntityInline entity => new Run(entity.Transcoded.ToString()),
        MdInlines.HtmlInline html => html.Tag.Replace(" ", string.Empty).Equals("<br/>", StringComparison.OrdinalIgnoreCase)
            || html.Tag.Equals("<br>", StringComparison.OrdinalIgnoreCase) ? new LineBreak() : null,
        MdInlines.AutolinkInline autolink => new Run(autolink.Url)
        {
            Foreground = theme.Accent,
            TextDecorations = TextDecorations.Underline,
        },
        MdTaskLists.TaskList task => new Run(task.Checked ? "☑ " : "☐ ") { Foreground = theme.Accent },
        MdInlines.EmphasisInline emphasis => RenderEmphasis(emphasis, theme),
        MdInlines.LinkInline { IsImage: true } image => RenderImage(image, theme),
        MdInlines.LinkInline link => RenderLink(link, theme),
        MdInlines.ContainerInline container => RenderSpan(container, theme),
        _ => null,
    };

    private static Span RenderSpan(MdInlines.ContainerInline container, MarkdownTheme theme)
    {
        var span = new Span();
        AddInlines(container, span.Inlines, theme);
        return span;
    }

    private static Span RenderEmphasis(MdInlines.EmphasisInline emphasis, MarkdownTheme theme)
    {
        var span = RenderSpan(emphasis, theme);
        switch (emphasis.DelimiterChar)
        {
            case '~':
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case '=':
                span.Background = theme.Highlight;
                break;
            case '+':
                span.TextDecorations = TextDecorations.Underline;
                break;
            default:
                if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.Bold;
                }
                else
                {
                    span.FontStyle = FontStyles.Italic;
                }

                break;
        }

        return span;
    }

    private static Span RenderLink(MdInlines.LinkInline link, MarkdownTheme theme)
    {
        var span = RenderSpan(link, theme);
        span.Foreground = theme.Accent;
        span.TextDecorations = TextDecorations.Underline;
        return span;
    }

    private static Inline RenderImage(MdInlines.LinkInline link, MarkdownTheme theme)
    {
        var alt = string.Concat(Md.MarkdownObjectExtensions.Descendants<MdInlines.LiteralInline>(link).Select(literal => literal.Content.ToString()));
        var uri = theme.ResolveImage(link.Url ?? string.Empty);
        var source = uri != null ? LoadImage(uri) : null;

        if (source == null)
        {
            return new Run($"[🖼 {(string.IsNullOrEmpty(alt) ? link.Url : alt)}]") { Foreground = theme.Muted };
        }

        var image = new Image
        {
            Source = source,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            MaxWidth = theme.ContentWidth,
            ToolTip = string.IsNullOrEmpty(alt) ? null : alt,
        };

        var attributes = link.TryGetAttributes();
        var width = ReadSize(attributes, "width");
        var height = ReadSize(attributes, "height");

        if (width.HasValue)
        {
            image.Width = Math.Min(width.Value, theme.ContentWidth);
        }

        if (height.HasValue)
        {
            image.Height = height.Value;
        }

        if (!width.HasValue && !height.HasValue)
        {
            image.StretchDirection = StretchDirection.DownOnly;
        }

        return new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static double? ReadSize(HtmlAttributes? attributes, string name)
    {
        var value = ReadAttribute(attributes, name);
        return value != null && TryParseNumber(value.Replace("px", string.Empty), out var result) ? result : null;
    }

    private static string? ReadAttribute(HtmlAttributes? attributes, string name)
    {
        var value = attributes?.Properties?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private enum TableBorders
    {
        All,
        None,
        Rows,
        Outer,
    }

    private static BitmapSource? LoadImage(Uri uri)
    {
        try
        {
            if (!uri.IsFile)
            {
                return new BitmapImage(uri);
            }

            var path = uri.LocalPath;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_imageCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            {
                return cached.Image;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();

            _imageCache[path] = (stamp, bitmap);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
