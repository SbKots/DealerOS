using DealerOS.Modules.Deals.Application;
using DealerOS.Modules.Deals.Domain;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DealerOS.Api.Infrastructure;

public sealed class PdfSharpDealPdfGenerator : IDealPdfGenerator
{
    private static readonly object FontLock = new();

    public byte[] Generate(DealPdfModel model)
    {
        EnsureFontResolver();
        using var document = new PdfDocument();
        document.Info.Title = model.Type == DealDocumentType.SaleContract
            ? "DealerOS demo sale contract" : "DealerOS demo handover act";
        document.Info.Subject = "DEMO TEMPLATE — NOT LEGALLY REVIEWED — NO ELECTRONIC SIGNATURE";
        var page = document.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        var title = new XFont("DealerOS", 17, XFontStyleEx.Bold);
        var body = new XFont("DealerOS", 10, XFontStyleEx.Regular);
        var small = new XFont("DealerOS", 8, XFontStyleEx.Regular);
        var width = page.Width.Point - 80;
        var y = 42d;
        Draw(graphics, model.Type == DealDocumentType.SaleContract
            ? "ДЕМО-ДОГОВОР КУПЛИ-ПРОДАЖИ" : "ДЕМО-АКТ ПРИЁМА-ПЕРЕДАЧИ", title, ref y, width, 28);
        Draw(graphics, "Демонстрационный шаблон. Не является юридически проверенной формой или электронной подписью.",
            small, ref y, width, 30);
        Draw(graphics, $"Номер: {model.DocumentNumber}", body, ref y, width);
        Draw(graphics, $"Версия шаблона: {model.TemplateVersion}", body, ref y, width);
        Draw(graphics, $"Deal ID: {model.DealId}", body, ref y, width);
        Draw(graphics, $"Клиент: {model.CustomerName}", body, ref y, width);
        Draw(graphics, $"Автомобиль snapshot: {model.VehicleSnapshotJson}", small, ref y, width, 45);
        Draw(graphics, $"Коммерческие строки snapshot: {model.LineItemsJson}", small, ref y, width, 45);
        Draw(graphics, $"Итог: {model.FinalTotalAmount:N2} {model.Currency}", body, ref y, width);
        Draw(graphics, $"Сформирован: {model.GeneratedAt:O}", small, ref y, width);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void Draw(XGraphics graphics, string text, XFont font, ref double y, double width,
        double height = 20)
    {
        graphics.DrawString(text, font, XBrushes.Black, new XRect(40, y, width, height), XStringFormats.TopLeft);
        y += height + 4;
    }

    private static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not null) return;
        lock (FontLock)
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new DealerOsFontResolver();
    }
}

internal sealed class DealerOsFontResolver : IFontResolver
{
    private const string FaceName = "DealerOS-Regular";
    private readonly byte[] _font = File.ReadAllBytes(FindFont());
    public byte[]? GetFont(string faceName) => faceName == FaceName ? _font : null;
    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(FaceName, false, false);
    private static string FindFont()
    {
        var candidates = new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("DealerOS PDF font is unavailable.");
    }
}
