using System;
using Cairo;
using Vintagestory.API.Client;

namespace SpellsAndRunes.HUD;

public class HudChickenCounter : HudElement
{
    private int _count;
    private double _drawScale = 1.0;

    public HudChickenCounter(ICoreClientAPI capi) : base(capi)
    {
        Compose();
        TryOpen();
    }

    public void SetCount(int count)
    {
        _count = count;
        Compose();
        TryOpen();
    }

    private void Compose()
    {
        _drawScale = Math.Clamp(capi.Render.FrameHeight / 1080.0, 0.60, 1.40);
        double width = 220 * _drawScale;
        double height = 36 * _drawScale;

        var db = ElementBounds.Fixed(EnumDialogArea.LeftTop, 12, 12, width, height);
        var cb = ElementBounds.Fixed(0, 0, width, height);

        SingleComposer = capi.Gui
            .CreateCompo("spellsandrunes:hud-chicken", db)
            .AddDynamicCustomDraw(cb, Draw, "canvas")
            .Compose();
    }

    private void Draw(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        ctx.Scale(_drawScale, _drawScale);

        const double w = 220;
        const double h = 36;
        const double r = 6;

        ctx.SetSourceRGBA(0.04, 0.03, 0.07, 0.88);
        RoundRect(ctx, 0, 0, w, h, r);
        ctx.Fill();

        ctx.SetSourceRGBA(0.85, 0.72, 0.30, 0.55);
        ctx.LineWidth = 1.1;
        RoundRect(ctx, 0.6, 0.6, w - 1.2, h - 1.2, r);
        ctx.Stroke();

        ctx.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        ctx.SetSourceRGBA(0.95, 0.92, 0.80, 0.95);

        string text = $"Poultry Sacrificed:  {_count}";
        ctx.MoveTo(10, 22);
        ctx.ShowText(text);
    }

    private static void RoundRect(Context ctx, double x, double y, double w, double h, double r)
    {
        double x2 = x + w;
        double y2 = y + h;
        ctx.NewSubPath();
        ctx.Arc(x2 - r, y + r, r, -Math.PI / 2, 0);
        ctx.Arc(x2 - r, y2 - r, r, 0, Math.PI / 2);
        ctx.Arc(x + r, y2 - r, r, Math.PI / 2, Math.PI);
        ctx.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        ctx.ClosePath();
    }
}
