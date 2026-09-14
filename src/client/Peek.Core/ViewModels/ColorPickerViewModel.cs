using ReactiveUI;
using System.Collections.ObjectModel;
using Peek.Core.Models;
using Peek.Core.Abstractions;
using ReactiveUI.Primitives;

namespace Peek.Core.ViewModels;

public class ColorPickerViewModel : ReactiveObject
{
    private static readonly string[] RawPalette =
    [
        "#39FF14", "#00FF41", "#7FFF00", "#ADFF2F",
        "#00FFFF", "#00BFFF", "#1E90FF", "#6495ED",
        "#FF4500", "#FF6347", "#FF69B4", "#FF1493",
        "#FFD700", "#FFA500", "#FF8C00", "#FFFF00",
        "#EE82EE", "#DA70D6", "#BA55D3", "#9400D3",
        "#FFFFFF", "#A0A0A0", "#505050", "#202020",
    ];
    private ColorModel _selectedColor = new(255, 57, 255, 20);
    private readonly IColorChangedNotify _colorChanged;

    public ColorPickerViewModel(IColorChangedNotify colorChanged)
    {
        _colorChanged = colorChanged;
        foreach (var hex in RawPalette)
        {
            var swatch = new PaletteSwatchViewModel(ColorModel.Parse(hex));

            swatch.Selected += color =>
            {
                SelectedColor = color;
                _colorChanged.ChangeColor(color);
            };

            Swatches.Add(swatch);
        }

        this.WhenAnyValue(x => x.SelectedColor)
            .Subscribe(UpdateSelection);
    }

    public ObservableCollection<PaletteSwatchViewModel> Swatches { get; } = [];

    public string HexLabel => SelectedColor.ToHex();

    public ColorModel SelectedColor
    {
        get => _selectedColor;
        set => this.RaiseAndSetIfChanged(ref _selectedColor, value);
    }

    private void UpdateSelection(ColorModel color)
    {
        foreach (var s in Swatches)
        {
            s.UpdateSelected(color);
        }
    }
}