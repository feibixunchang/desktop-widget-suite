using System.Windows.Forms;

namespace Shared;

internal sealed class DoubleBufferedPanel : Panel
{
	public DoubleBufferedPanel()
	{
		DoubleBuffered = true;
	}
}
