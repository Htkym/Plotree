using CommunityToolkit.Mvvm.ComponentModel;
using Plotree.Models;

namespace Plotree.ViewModels;

/// <summary>Tri-state checklist row for assigning one project tag to the current node selection.</summary>
public partial class TagOptionViewModel : ObservableObject
{
    private readonly MainPageViewModel _owner;

    public TagOptionViewModel(ColorTag model, MainPageViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public ColorTag Model { get; }
    public string Name => Model.Name;
    public string Color => Model.Color;
    public string AutomationId => $"TagOption_{Name}";
    public bool IsThreeState => _owner.IsMultiNodeSelected;

    [ObservableProperty]
    public partial bool? IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool? value) => _owner.OnTagToggled(this, value);

    internal void RefreshSelectionMode() => OnPropertyChanged(nameof(IsThreeState));
}
