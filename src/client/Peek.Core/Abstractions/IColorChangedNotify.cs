using Peek.Core.Models;

namespace Peek.Core.Abstractions;

public interface IColorChangedNotify
{
    void ChangeColor(ColorModel colorModel);
}
