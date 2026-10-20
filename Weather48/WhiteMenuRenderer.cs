namespace XiaoPuHeWeather;

public sealed class WhiteMenuRenderer : ToolStripProfessionalRenderer
{
    public WhiteMenuRenderer()
        : base(new WhiteMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = Color.Black;
        base.OnRenderItemText(e);
    }

    private sealed class WhiteMenuColorTable : ProfessionalColorTable
    {
        public override Color ImageMarginGradientBegin => Color.White;

        public override Color ImageMarginGradientMiddle => Color.White;

        public override Color ImageMarginGradientEnd => Color.White;

        public override Color MenuBorder => Color.FromArgb(190, 190, 190);

        public override Color MenuItemBorder => Color.FromArgb(190, 190, 190);

        public override Color MenuItemSelected => Color.FromArgb(232, 240, 254);

        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 240, 254);

        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(232, 240, 254);

        public override Color SeparatorDark => Color.FromArgb(190, 190, 190);

        public override Color SeparatorLight => Color.White;

        public override Color ToolStripDropDownBackground => Color.White;
    }
}
