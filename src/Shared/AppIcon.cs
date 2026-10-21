using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Shared
{
internal static class AppIcon
    {
        // 不能写死资源名：图标是嵌在各挂件自己的程序集里的，
        // 名字各不相同（desktopcalendar.ico / rsswidget.ico / desktopvideo.ico ...），
        // 写死一个会让别的挂件永远找不到图标，只能退回到系统通用图标。
        // 现在改成在入口程序集里找任意一个 *.ico 资源。
        private static Icon LoadFromAssembly(Assembly assembly, int size)
        {
            if (assembly == null)
            {
                return null;
            }

            try
            {
                string[] names = assembly.GetManifestResourceNames();
                Array.Sort(names, StringComparer.OrdinalIgnoreCase);

                foreach (string name in names)
                {
                    if (!name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using (Stream stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null)
                        {
                            continue;
                        }

                        using (var buffer = new MemoryStream())
                        {
                            stream.CopyTo(buffer);
                            buffer.Position = 0;
                            return new Icon(buffer, size, size);
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static Icon Load(int size)
        {
            Assembly entry = Assembly.GetEntryAssembly();

            Icon icon = LoadFromAssembly(entry, size);
            if (icon != null)
            {
                return icon;
            }

            icon = FromExecutable(size);
            if (icon != null)
            {
                return icon;
            }

            return (Icon)SystemIcons.Application.Clone();
        }

private static Icon FromExecutable(int size)
        {
            try
            {
                string path = Assembly.GetEntryAssembly() == null
                    ? null
                    : Assembly.GetEntryAssembly().Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                Icon extracted = size <= 32 ? Icon.ExtractAssociatedIcon(path) : null;
                if (extracted != null)
                {
                    return extracted;
                }

                using (var bitmap = new Bitmap(size, size))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    Icon large = Icon.ExtractAssociatedIcon(path);
                    if (large != null)
                    {
                        using (Bitmap source = large.ToBitmap())
                        {
                            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
                        }

                        large.Dispose();
                    }

                    return Icon.FromHandle(bitmap.GetHicon());
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

