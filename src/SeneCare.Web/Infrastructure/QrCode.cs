using QRCoder;

namespace SeneCare.Web.Infrastructure;

/// <summary>QR code as inline SVG (a few KB, crisp at any size, works offline once the page is loaded).</summary>
public static class QrCode
{
    public static string Svg(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(
            new System.Drawing.Size(240, 240), "#1f1b16", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
        return svg;
    }
}
