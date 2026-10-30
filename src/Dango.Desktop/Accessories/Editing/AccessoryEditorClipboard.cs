using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Accessories.Editing;

// Only explicit editor copy/paste messages call this boundary. Never poll, inspect
// other formats, or persist clipboard contents. Tests inject a memory-only port.
internal interface IAccessoryEditorClipboard
{
    void Write(byte[] fragment, byte[] png);
    byte[]? ReadFragment();
}

internal sealed class AccessoryEditorClipboard : IAccessoryEditorClipboard
{
    internal const string FragmentFormat = "Dango.Accessory.EditableFragment.v1";
    public void Write(byte[] fragment, byte[] png) => System.Windows.Clipboard.SetDataObject(CreateDataObject(fragment, png), true);

    // Build all formats before replacing the system clipboard in one operation.
    internal static System.Windows.DataObject CreateDataObject(byte[] fragment, byte[] png)
    {
        using var pixels = new MemoryStream(png, false);
        var image = BitmapFrame.Create(pixels, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze();
        var data = new System.Windows.DataObject();
        data.SetData(FragmentFormat, new MemoryStream(fragment, false), false);
        data.SetData("PNG", new MemoryStream(png, false), false);
        data.SetImage(image); // Native image consumers often request CF_BITMAP.
        return data;
    }

    public byte[]? ReadFragment()
    {
        var data = System.Windows.Clipboard.GetDataObject();
        if (data?.GetDataPresent(FragmentFormat, false) != true) return null;
        var value = data.GetData(FragmentFormat, false);
        if (value is not Stream stream || !stream.CanRead || !stream.CanSeek ||
            stream.Length is < 1 or > AccessoryEditorBridge.MaximumTransferBytes)
            throw new InvalidDataException("Invalid editable clipboard fragment.");
        stream.Position = 0;
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
