using System;
using System.Drawing;
using System.Windows.Forms;
using Shared;

namespace DesktopNotes
{
    internal sealed class NoteEditEventArgs : EventArgs
    {
        public string Text;
    }

    /// <summary>
    /// 便签编辑面板。
    ///
    /// 卡片是分层窗口，挂不了子控件，所以真正的输入放在这里 —— 这是个普通窗口，
    /// 用的是真的 TextBox，中文输入法、粘贴、撤销都正常。
    /// </summary>
    internal sealed class NoteEditPanel : PopupPanel
    {
        private readonly TextBox box = UiTools.TextBox(true);
        private readonly Button save = UiTools.Button("保存", true);
        private readonly Button cancel = UiTools.Button("取消", false);

        /// <summary>点「保存」时抛出，带改好之后的文字。</summary>
        public event EventHandler<NoteEditEventArgs> Committed;

        public NoteEditPanel(string text, bool isNew)
        {
            PanelTitle = isNew ? "新建便签" : "编辑便签";

            box.Multiline = true;
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.AcceptsReturn = true;
            box.Text = text ?? string.Empty;

            save.Click += delegate { Commit(); };
            cancel.Click += delegate { ClosePopup(); };

            Body.Controls.Add(box);
            Body.Controls.Add(save);
            Body.Controls.Add(cancel);

            SetContentSize(420, 330);
            LayoutContent(414);
        }

        public void FocusText()
        {
            try
            {
                box.Focus();
                box.SelectionStart = box.TextLength;
            }
            catch
            {
            }
        }

        private void Commit()
        {
            EventHandler<NoteEditEventArgs> handler = Committed;
            if (handler != null)
            {
                handler(this, new NoteEditEventArgs { Text = box.Text });
            }

            ClosePopup();
        }

        protected override void LayoutContent(int contentWidth)
        {
            float scale = ContentScale(contentWidth);
            int width = Math.Max(160, contentWidth);
            int gap = Math.Max(4, (int)Math.Round(6 * scale));
            int buttonHeight = UiTools.ControlHeight(scale);
            int buttonWidth = Math.Max(76, (int)Math.Round(92 * scale));

            int y = 2;
            box.Location = new Point(2, y);
            box.Size = new Size(width - 4, Math.Max(90, (int)Math.Round(170 * scale)));
            y += box.Height + gap;

            cancel.Size = new Size(buttonWidth, buttonHeight);
            cancel.Location = new Point(2 + width - 4 - buttonWidth, y);
            save.Size = new Size(buttonWidth, buttonHeight);
            save.Location = new Point(cancel.Left - gap - buttonWidth, y);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+Enter 保存，Esc 取消（Esc 基类已经处理）
            if (keyData == (Keys.Control | Keys.Enter))
            {
                Commit();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
