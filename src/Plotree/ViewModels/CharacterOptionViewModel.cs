using CommunityToolkit.Mvvm.ComponentModel;
using Plotree.Models;

namespace Plotree.ViewModels;

/// <summary>Tri-state checklist row for assigning a character to the current node selection.</summary>
public partial class CharacterOptionViewModel : ObservableObject
{
    private readonly MainPageViewModel _owner;

    public CharacterOptionViewModel(Character model, MainPageViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public Character Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string AutomationId => $"CharacterOption_{Id}";
    public string GroupSummary => _owner.GetCharacterGroupSummary(Model);
    public bool IsThreeState => _owner.IsMultiNodeSelected;

    [ObservableProperty]
    public partial bool? IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool? value) => _owner.OnCharacterToggled(this, value);

    internal void RefreshGroupSummary() => OnPropertyChanged(nameof(GroupSummary));

    internal void RefreshSelectionMode() => OnPropertyChanged(nameof(IsThreeState));
}
