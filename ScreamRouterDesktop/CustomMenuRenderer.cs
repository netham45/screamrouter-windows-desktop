using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreamRouterDesktop
{
    public class CustomMenuRenderer : ToolStripProfessionalRenderer
    {
        public CustomMenuRenderer() : base(new CustomColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
                base.OnRenderMenuItemBackground(e);
            else
            {
                Rectangle rc = new Rectangle(Point.Empty, e.Item.Size);
                using (SolidBrush brush = new SolidBrush(Color.LightBlue))
                    e.Graphics.FillRectangle(brush, rc);
            }
        }
    }

    public class CustomColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected
        {
            get { return Color.LightBlue; }
        }

        public override Color MenuItemBorder
        {
            get { return Color.Transparent; }
        }
    }
}