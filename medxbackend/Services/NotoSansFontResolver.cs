using PdfSharp.Fonts;

namespace medxbackend.Services;

public sealed class NotoSansFontResolver : IFontResolver
{
    private const string RegularFaceName = "NotoSansRegular";
    private const string BoldFaceName = "NotoSansBold";
    private const string SimplifiedChineseFaceName = "NotoSansSimplifiedChinese";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (string.Equals(familyName, "Noto Sans SC", StringComparison.OrdinalIgnoreCase))
        {
            return new FontResolverInfo(SimplifiedChineseFaceName, false, isItalic);
        }

        if (!string.Equals(familyName, "Noto Sans", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FontResolverInfo(isBold ? BoldFaceName : RegularFaceName, false, isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        var resourceName = faceName switch
        {
            RegularFaceName => "medxbackend.Fonts.NotoSans-Regular.ttf",
            BoldFaceName => "medxbackend.Fonts.NotoSans-Bold.ttf",
            SimplifiedChineseFaceName => "medxbackend.Fonts.NotoSansSC-VF.ttf",
            _ => null
        };

        if (resourceName is null)
        {
            return null;
        }

        using var fontStream = typeof(NotoSansFontResolver).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' was not found.");
        using var memoryStream = new MemoryStream();
        fontStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}